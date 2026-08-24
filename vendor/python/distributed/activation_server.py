"""One stage of the pipeline, listening for activations.

Every machine in the pipeline runs one of these, and they are all the same program. What differs
is the assignment each one is handed: which layers it holds, whether it embeds, whether it has
the head. A stage reads a tensor off its socket, runs its own layers over it, and writes the
result to the next stage. The last stage has nowhere onward to write, so it samples a token and
sends that back to the host instead.

The stage does not know how many stages there are, which model this is, or what the request is
for. It knows its own layers and the address of the machine after it. That is deliberate: it is
what lets the same program be stage 0 on one machine and stage 3 on another, and it is why
adding a machine changes a plan rather than any code here.

A machine starts listening before it has been given anything to hold, because it has to be
reachable in order to be asked what it has. So a server begins unassigned, answers questions
about itself, and is handed its layers afterwards. Loading is minutes of work and being asked
about free memory has to be possible during them.

Compute runs on a worker thread rather than on the event loop. A forward pass over forty layers
is hundreds of milliseconds during which the loop would otherwise be unable to read its socket,
and torch gives up the interpreter lock while the GPU works, so the thread costs nothing and buys
a stage that can still answer while it is busy.
"""

from __future__ import annotations

import asyncio
import logging
import time
import traceback
from datetime import datetime
from typing import Any, Protocol, runtime_checkable

import torch

from . import protocol
from .config import NodeInfo, StageAssignment
from .protocol import Frame

_log = logging.getLogger(__name__)

#: How hard to try to reach the next stage while serving a request. Deliberately not the patient
#: retry that bringing a pipeline up uses. During startup a stage that is not answering yet is
#: usually still reading weights off a disk and waiting minutes for it is right. During a request
#: every stage has already reported itself loaded, so one that does not answer is one that has
#: died, and waiting is time spent on an answer that is not coming.
_REQUEST_CONNECT_ATTEMPTS = 3
_REQUEST_CONNECT_PAUSE = 0.5


@runtime_checkable
class StageRunner(Protocol):
    """What a stage server needs from whatever actually holds the weights.

    Three methods, and none of them mention a socket. The pipeline is a transport concern and
    running layers is a model concern, and keeping them apart is what made it possible to put a
    pipeline on the wire before there was a model to put through it.
    """

    def step(
        self,
        request_id: str,
        incoming: torch.Tensor,
        position_offset: int,
        sampling: dict[str, Any],
    ) -> torch.Tensor:
        """Runs this stage's share for one request at one step.

        The first stage is handed token ids and returns hidden states. A middle stage is handed
        hidden states and returns hidden states. The last stage is handed hidden states and
        returns the single token id it sampled, which is why the return type is a tensor for all
        three rather than something different for the end of the line.

        ``position_offset`` is how many tokens this request has already been through, which is
        what lets each stage position its own rotary embeddings without being told anything about
        the stages before it.
        """
        ...

    def release(self, request_id: str) -> None:
        """Drops everything held for a request. Called when it finishes and when it fails."""
        ...

    def describe(self) -> str:
        """What this runner is holding, for the log line written when a stage comes up."""
        ...


class StageServer:
    """The socket half of a stage: accepts activations, runs them, passes them on."""

    def __init__(
        self,
        node: NodeInfo,
        assignment: StageAssignment | None = None,
        runner: StageRunner | None = None,
        next_stage: StageAssignment | None = None,
        return_to: StageAssignment | None = None,
    ) -> None:
        """
        Args:
            node: which machine this is, and where it listens. Known before anything is assigned.
            assignment: what this machine holds, when that has been decided.
            runner: the thing that actually runs the layers.
            next_stage: where output goes, or nothing if this is the last stage.
            return_to: where a sampled token goes. Only the last stage uses it, and it is the
                host, which is stage 0.
        """
        self._node = node
        self._assignment = assignment
        self._runner = runner
        self._next = next_stage
        self._return_to = return_to

        self._server: asyncio.Server | None = None

        # One connection per machine this stage talks to, opened once and kept. Reconnecting per
        # request would add a handshake to every token. There are at most two of these: the stage
        # after this one, and the host. They are held separately because a middle stage whose next
        # stage has died still has to be able to say so to the host, and it cannot say it down the
        # link that just broke.
        self._links: dict[str, tuple[asyncio.StreamReader, asyncio.StreamWriter]] = {}
        self._link_locks: dict[str, asyncio.Lock] = {}
        self._locks_guard = asyncio.Lock()
        self._watchers: set[asyncio.Task[None]] = set()

        # Connections other stages have opened to this one. They are held so that shutdown can
        # close them: asyncio's own wait_closed does not return until every accepted connection's
        # handler has finished, and a handler here finishes only when its peer goes away, so a
        # stage waiting politely for that waits for the machine it is trying to shut down before.
        self._accepted: set[asyncio.StreamWriter] = set()
        self._closing = False

        # Only ever read by the diagnostic on a lost link. A link that has been up for
        # milliseconds died during a handshake; one that has been up for minutes died some other
        # way, and the two are not the same fault.
        self._link_opened: dict[str, float] = {}
        self._link_opened_at: dict[str, str] = {}
        self._forwards_seen = 0

        # The model is one object and the cache for a request is appended to in order, so two
        # forwards must not overlap inside it. This is the whole of the concurrency story.
        self._compute_lock = asyncio.Lock()

        # Set by the host so a token arriving from the last stage reaches the request waiting for
        # it. Every other stage leaves this alone and never sees a token frame.
        self._on_token: Any = None

        # Set on a contributing machine so that a plan arriving from the host is acted on.
        self._on_load: Any = None

    @property
    def node(self) -> NodeInfo:
        return self._node

    @property
    def assignment(self) -> StageAssignment | None:
        return self._assignment

    @property
    def is_assigned(self) -> bool:
        return self._assignment is not None and self._runner is not None

    @property
    def _label(self) -> str:
        """How this server names itself in a log, before and after it has an assignment."""
        if self._assignment is None:
            return f"{self._node.display_name} (unassigned)"

        return f"stage {self._assignment.stage_index}"

    def on_token(self, handler: Any) -> None:
        """Registers who receives tokens coming back from the end of the pipeline."""
        self._on_token = handler

    def on_load(self, handler: Any) -> None:
        """Registers what happens when the host sends a plan. Contributing machines only."""
        self._on_load = handler

    def attach(
        self,
        assignment: StageAssignment,
        runner: StageRunner,
        next_stage: StageAssignment | None,
        return_to: StageAssignment | None,
    ) -> None:
        """Gives an already listening server its share of the model."""
        self._assignment = assignment
        self._runner = runner
        self._next = next_stage
        self._return_to = return_to

        _log.info("%s holds %s", self._label, runner.describe())

    async def start(self) -> None:
        """Begins listening. Does not load anything: a runner is attached separately."""
        self._server = await asyncio.start_server(self._serve, self._node.host, self._node.port)

        _log.info("%s listening on %s", self._label, self._node.address)

    async def stop(self) -> None:
        """Closes the listener and every outbound connection, in that order.

        ``_closing`` goes up first so that the watchers, which are about to see their connections
        end, report a shutdown as a shutdown rather than as the machine on the other side dying.
        """
        self._closing = True

        if self._server is not None:
            self._server.close()

            for writer in list(self._accepted):
                writer.close()

            self._accepted.clear()

            await self._server.wait_closed()
            self._server = None

        for watcher in list(self._watchers):
            watcher.cancel()

        self._watchers.clear()

        for _, writer in list(self._links.values()):
            writer.close()

            try:
                await writer.wait_closed()
            except (ConnectionError, OSError):
                pass

        self._links.clear()

    async def _serve(self, reader: asyncio.StreamReader, writer: asyncio.StreamWriter) -> None:
        peer = writer.get_extra_info("peername")
        self._accepted.add(writer)

        try:
            while True:
                frame = await protocol.read(reader, device="cpu")
                await self._dispatch(frame, writer)
        except protocol.Disconnected:
            _log.debug("%s: %s went away", self._label, peer)
        except protocol.ProtocolError as error:
            _log.warning("%s: %s sent something unreadable: %s", self._label, peer, error)
        finally:
            self._accepted.discard(writer)
            writer.close()

    async def _dispatch(self, frame: Frame, writer: asyncio.StreamWriter) -> None:
        if frame.kind == protocol.FORWARD:
            await self._on_forward(frame, writer)
        elif frame.kind == protocol.RELEASE:
            await self._on_release(frame)
        elif frame.kind == protocol.HELLO:
            await protocol.write(writer, Frame(protocol.WELCOME, self._welcome()))
        elif frame.kind == protocol.LOAD:
            await self._on_load_frame(frame, writer)
        elif frame.kind in (protocol.TOKEN, protocol.ERROR):
            await self._deliver_token(frame)
        else:
            await protocol.write(writer, protocol.error_frame(
                f"{self._label} has no use for a {frame.kind!r} frame"
            ))

    async def _on_load_frame(self, frame: Frame, writer: asyncio.StreamWriter) -> None:
        """The host has sent a plan. Loading happens off the loop and can take minutes."""
        if self._on_load is None:
            await protocol.write(writer, protocol.error_frame(
                f"{self._label} does not take assignments"
            ))
            return

        try:
            await self._on_load(frame.body)
        except Exception as error:  # noqa: BLE001 - the host needs the reason, not a closed socket
            _log.exception("%s could not take its assignment", self._label)
            await protocol.write(writer, Frame(protocol.LOADED, {
                "node_id": self._node.node_id,
                "loaded": False,
                "reason": str(error),
            }))
            return

        await protocol.write(writer, Frame(protocol.LOADED, {
            "node_id": self._node.node_id,
            "loaded": True,
            "holding": self._runner.describe() if self._runner is not None else "",
        }))

    async def _on_forward(self, frame: Frame, writer: asyncio.StreamWriter) -> None:
        request_id = str(frame.body.get("request_id", ""))

        if not self.is_assigned:
            await protocol.write(writer, protocol.error_frame(
                f"{self._label} was sent work before it was given any layers",
                request_id=request_id,
            ))
            return

        if frame.tensor is None:
            await protocol.write(writer, protocol.error_frame(
                "a forward frame arrived with no tensor", request_id=request_id
            ))
            return

        self._forwards_seen += 1

        try:
            produced = await self.run_step(
                request_id=request_id,
                incoming=frame.tensor,
                position_offset=int(frame.body.get("position_offset", 0)),
                sampling=dict(frame.body.get("sampling") or {}),
            )
        except Exception as error:  # noqa: BLE001 - the reason has to reach the host intact
            _log.exception("%s failed on request %s", self._label, request_id)
            self._safely_release(request_id)
            await self._report_failure(request_id, error)
            return

        onward = Frame(
            kind=protocol.TOKEN if self._next is None else protocol.FORWARD,
            body=self._onward_body(frame, produced),
            tensor=None if self._next is None else produced,
        )

        await self._send_onward(onward)

    def _onward_body(self, frame: Frame, produced: torch.Tensor) -> dict[str, Any]:
        """What travels with the tensor to the next stage, or with the token back to the host."""
        request_id = str(frame.body.get("request_id", ""))

        if self._next is not None:
            # Everything the stages after this one need is passed straight through, unread. A
            # middle stage has no use for the sampling settings and must not drop them either,
            # because the stage that does need them is at the far end of the chain.
            return dict(frame.body)

        return {"request_id": request_id, "token_id": int(produced.reshape(-1)[0].item())}

    async def run_step(
        self,
        request_id: str,
        incoming: torch.Tensor,
        position_offset: int,
        sampling: dict[str, Any],
    ) -> torch.Tensor:
        """Runs this stage's layers, off the event loop and one request at a time.

        Public because the host runs its own stage 0 through this rather than opening a socket to
        itself. A loopback connection would work and would add a copy and a hop to every token
        for the sake of making the code look uniform.
        """
        if self._runner is None:
            raise RuntimeError(f"{self._label} has no layers to run.")

        async with self._compute_lock:
            return await asyncio.to_thread(
                self._runner.step, request_id, incoming, position_offset, sampling
            )

    async def send_onward(self, frame: Frame) -> None:
        """Sends a frame down the pipeline. The host uses this to start a request."""
        await self._send_onward(frame)

    def release(self, request_id: str) -> None:
        """Drops what this stage holds for a request, without disturbing the others."""
        self._safely_release(request_id)

    async def _on_release(self, frame: Frame) -> None:
        request_id = str(frame.body.get("request_id", ""))

        self._safely_release(request_id)

        # The release has to reach every stage, and only the host knows the whole chain is behind
        # it. Passing it along means the host tells stage 1 and the rest follow.
        if self._next is not None:
            await self._send_onward(Frame(protocol.RELEASE, {"request_id": request_id}))

    def _safely_release(self, request_id: str) -> None:
        if self._runner is None:
            return

        try:
            self._runner.release(request_id)
        except Exception:  # noqa: BLE001 - a cache that will not drop must not stop the pipeline
            _log.exception("%s could not release %s", self._label, request_id)

    async def _report_failure(self, request_id: str, error: Exception) -> None:
        """Sends the reason to whoever is waiting, rather than leaving them on a dead request.

        A stage that fails silently is a request that hangs until something times out, and the
        timeout says nothing about what went wrong. The reason travels the same path a token
        would.
        """
        await self._pass_back(protocol.error_frame(
            f"{self._label} on {self._node.node_id} failed: {error}",
            request_id=request_id,
            stage=self._assignment.stage_index if self._assignment else -1,
        ))

    async def _deliver_token(self, frame: Frame) -> None:
        """Hands a frame coming back from the end of the pipeline to whoever is waiting."""
        if self._on_token is None:
            _log.warning("%s received %s and has nobody to give it to", self._label, frame.kind)
            return

        result = self._on_token(frame)

        if asyncio.iscoroutine(result):
            await result

    async def _send_onward(self, frame: Frame) -> None:
        """To the next stage, or back to the host when this is the end of the line."""
        target = self._next if self._next is not None else self._return_to

        if target is None:
            # The last stage of a single stage pipeline, which is the host talking to itself.
            await self._deliver_token(frame)
            return

        await self._send_to(target, frame)

    async def _send_to(self, target: StageAssignment, frame: Frame) -> None:
        async with await self._lock_for(target.address):
            link = self._links.get(target.address)

            if link is None:
                reader, writer = await protocol.connect(
                    target.host,
                    target.port,
                    attempts=_REQUEST_CONNECT_ATTEMPTS,
                    pause=_REQUEST_CONNECT_PAUSE,
                )
                self._links[target.address] = (reader, writer)
                self._link_opened[target.address] = time.monotonic()
                self._link_opened_at[target.address] = datetime.now().isoformat(timespec="seconds")

                self._watch(reader, target)

                _log.info("%s connected to %s", self._label, target.address)
            else:
                _, writer = link

            try:
                await protocol.write(writer, frame)
            except (ConnectionError, OSError) as error:
                # The socket died between requests. Drop it so the next attempt reconnects
                # rather than writing into a closed handle forever.
                self._links.pop(target.address, None)

                raise protocol.Disconnected(
                    f"{target.address} could not be written to: {error}"
                ) from error

    async def _lock_for(self, address: str) -> asyncio.Lock:
        """One lock per machine talked to, so a slow connect to one cannot hold up the other.

        This matters exactly once and that once is the case worth getting right: a stage whose
        next stage has died is trying to reach the host while its onward connect is still
        retrying, and a shared lock would make the report wait for the timeout of the thing it is
        reporting.
        """
        async with self._locks_guard:
            return self._link_locks.setdefault(address, asyncio.Lock())

    def _watch(self, reader: asyncio.StreamReader, target: StageAssignment) -> None:
        """Keeps reading from an outbound connection so that its ending is noticed.

        Nothing normally comes back down a link to the next stage, which is exactly why it is
        worth reading: the only thing that ever arrives on it is the end of the connection, and
        that is the machine on the other side going away. Without this the socket is write only,
        a write into a dead peer's buffer succeeds, and the request that depended on it waits for
        a token that nobody is going to produce. That silence is the worst failure this can have,
        because it looks the same as a model thinking.
        """
        task = asyncio.create_task(self._watch_link(reader, target))

        self._watchers.add(task)
        task.add_done_callback(self._watchers.discard)

    async def _watch_link(self, reader: asyncio.StreamReader, target: StageAssignment) -> None:
        try:
            while True:
                frame = await protocol.read(reader, device="cpu")

                # An error coming back up the pipeline belongs to the host, not to this stage.
                if frame.kind in (protocol.ERROR, protocol.TOKEN):
                    await self._pass_back(frame)
        except asyncio.CancelledError:
            raise
        except protocol.ProtocolError as error:
            if self._closing:
                return

            self._links.pop(target.address, None)

            # This has fired once with the machine on the other side demonstrably alive and every
            # request around it succeeding, which does not match what this code does. Until that
            # is understood, the log carries everything needed to tell a real disconnection from
            # whatever that was: which exception it actually is rather than only its text, the
            # cause underneath it, how long the link had been up, whether the socket agrees that
            # it is gone, and the stack that got here. Nothing is corrected on the strength of a
            # guess, so the behaviour below is exactly what it was.
            _log.warning(
                "%s lost %s: %s\n"
                "  exception     %s\n"
                "  cause         %s\n"
                "  link age      %.1fs, opened at %s\n"
                "  reader eof    %s\n"
                "  transport     %s\n"
                "  assigned      %s\n"
                "  requests seen %d\n"
                "  stack:\n%s",
                self._label,
                target.address,
                error,
                type(error).__name__,
                f"{type(error.__cause__).__name__}: {error.__cause__}"
                if error.__cause__ is not None else "none reported",
                time.monotonic() - self._link_opened.get(target.address, time.monotonic()),
                self._link_opened_at.get(target.address, "not recorded"),
                _describe_eof(reader),
                _describe_transport(reader),
                self.is_assigned,
                self._forwards_seen,
                "".join(traceback.format_stack()).rstrip(),
            )

            await self._pass_back(protocol.error_frame(
                f"{self._label} lost the machine after it, {target.node_id} at "
                f"{target.address}. The pipeline has stopped.",
                stage=self._assignment.stage_index if self._assignment else -1,
                lost=target.node_id,
            ))

    async def _pass_back(self, frame: Frame) -> None:
        """Sends something toward the host, or hands it over if this is the host.

        A break has to reach whoever is waiting on a token, and that is always the host. Every
        stage either is the host or knows where it is.
        """
        try:
            if self._return_to is not None and self._return_to.node_id != self._node.node_id:
                await self._send_to(self._return_to, frame)
            else:
                await self._deliver_token(frame)
        except (protocol.ProtocolError, ConnectionError, OSError):
            _log.exception("%s could not pass a %s back toward the host",
                           self._label, frame.kind)

    def _welcome(self) -> dict[str, Any]:
        """What this machine is and what it has free, asked for rather than assumed."""
        fresh = local_node_info(self._node.node_id, self._node.host, self._node.port,
                                self._node.label)

        return {
            **fresh.to_dict(),
            "assigned": self.is_assigned,
            "holding": self._runner.describe() if self._runner is not None else "",
        }

def _describe_eof(reader: asyncio.StreamReader) -> str:
    """Whether the stream itself agrees that the other end is gone.

    Worth asking separately from the exception. A reader reporting end of file is a connection
    that really closed; one that does not, on a link that just reported itself lost, is the case
    that has been seen once and not explained.
    """
    try:
        return str(reader.at_eof())
    except Exception as error:  # noqa: BLE001 - a diagnostic must never raise
        return f"could not be asked: {error}"


def _describe_transport(reader: asyncio.StreamReader) -> str:
    """What the socket underneath says about itself, if it is still reachable at all."""
    try:
        transport = reader._transport  # noqa: SLF001 - diagnostics only, and it is the only way

        if transport is None:
            return "already detached"

        socket = transport.get_extra_info("socket")

        return (
            f"closing={transport.is_closing()}, "
            f"peer={transport.get_extra_info('peername')}, "
            f"socket={'closed' if socket is None else socket.fileno()}"
        )
    except Exception as error:  # noqa: BLE001 - a diagnostic must never raise
        return f"could not be read: {error}"


def local_node_info(node_id: str, host: str, port: int, label: str = "") -> NodeInfo:
    """What this machine has to offer, as it would report it to a coordinator.

    Free memory is asked of the driver rather than worked out from the card's size, because what
    matters is what is free now, on a machine that may already be running a game, a browser and
    the model this is meant to replace. A machine with no usable accelerator reports its system
    memory and says cpu, which is slow and honest.
    """
    if torch.cuda.is_available():
        index = torch.cuda.current_device()
        free, _total = torch.cuda.mem_get_info(index)

        return NodeInfo(
            node_id=node_id,
            host=host,
            port=port,
            vram_bytes=int(free),
            device=f"cuda:{index}",
            label=label or torch.cuda.get_device_name(index),
        )

    return NodeInfo(
        node_id=node_id,
        host=host,
        port=port,
        vram_bytes=_free_system_memory(),
        device="cpu",
        label=label,
    )


def _free_system_memory() -> int:
    """Free system memory, or nothing reported rather than a number that was made up.

    There is no way to ask for this in the standard library on Windows, and psutil is not in the
    locked environment. Windows answers through GlobalMemoryStatusEx, which ctypes can call
    without adding a dependency. Anywhere else this returns zero, and a node reporting zero is
    one the planner will refuse to place layers on, which is the correct outcome for a machine
    that cannot say how much room it has.
    """
    try:
        import ctypes

        class _Status(ctypes.Structure):
            _fields_ = [
                ("dwLength", ctypes.c_ulong),
                ("dwMemoryLoad", ctypes.c_ulong),
                ("ullTotalPhys", ctypes.c_ulonglong),
                ("ullAvailPhys", ctypes.c_ulonglong),
                ("ullTotalPageFile", ctypes.c_ulonglong),
                ("ullAvailPageFile", ctypes.c_ulonglong),
                ("ullTotalVirtual", ctypes.c_ulonglong),
                ("ullAvailVirtual", ctypes.c_ulonglong),
                ("ullAvailExtendedVirtual", ctypes.c_ulonglong),
            ]

        status = _Status()
        status.dwLength = ctypes.sizeof(_Status)

        if ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(status)):  # type: ignore[attr-defined]
            return int(status.ullAvailPhys)
    except (AttributeError, OSError):
        pass

    return 0
