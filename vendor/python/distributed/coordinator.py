"""Assembling a pipeline out of machines, and driving requests through it.

There are two roles and one program. A contributing machine runs a peer: it listens, says what it
has when asked, and loads whatever it is given. The host runs a coordinator: it asks each machine
what it has, plans the split, hands out the assignments, waits for everyone to report loaded, and
then holds stage 0 itself and answers requests.

The host is stage 0 rather than a separate conductor because a request already arrives there. Any
other arrangement sends the prompt to a machine, which sends it to the first stage, which is a
hop bought for symmetry.

Peers are named, not discovered. The end goal is a network of strangers and this is not it: for
now a machine is an address somebody typed or a mesh already established elsewhere. The seam is
that a peer is a ``NodeInfo``, so where that list comes from can change without any of this
changing. What must not be built here is a second discovery mechanism.
"""

from __future__ import annotations

import asyncio
import contextlib
import logging
import uuid
from typing import Any

import torch

from . import planner, protocol
from .activation_server import StageServer, local_node_info
from .config import (
    DEFAULT_MARGIN_BYTES,
    NodeInfo,
    PlanError,
    StageAssignment,
    StagePlan,
)
from .layer_map import build as build_layer_map
from .partial_loader import PartialStage
from .protocol import Frame

_log = logging.getLogger(__name__)

#: How long a request waits for a token before it is called a failure. A stage on a slow machine
#: can genuinely take seconds per token, and a stage that has died takes forever, so this is set
#: where a person would have given up rather than where a model would.
TOKEN_TIMEOUT_SECONDS = 300.0


class PipelineError(Exception):
    """The pipeline could not be assembled, or a request through it did not finish."""


class Peer:
    """A machine that contributes layers and is told what to hold.

    This is the whole of the contributing role: listen, answer what you have, load what you are
    given, run it. It never plans, never addresses another peer by name, and never decides
    anything about the model.
    """

    def __init__(self, node: NodeInfo) -> None:
        self._node = node
        self._server = StageServer(node=node)
        self._stage: PartialStage | None = None

        self._server.on_load(self._take_assignment)

    @property
    def node(self) -> NodeInfo:
        return self._node

    async def start(self) -> None:
        await self._server.start()

    async def stop(self) -> None:
        await self._server.stop()

    async def _take_assignment(self, body: dict[str, Any]) -> None:
        """Loads the layers the host has assigned to this machine.

        Loading happens on a worker thread. It reads tens of gigabytes and takes minutes, and the
        host is holding a socket open waiting to be told whether it worked, so the event loop has
        to stay able to answer.
        """
        plan = StagePlan.from_dict(body["plan"])
        assignment = plan.for_node(self._node.node_id)

        stage = PartialStage(plan.model_dir, assignment)

        await asyncio.to_thread(stage.load)

        self._stage = stage
        self._server.attach(
            assignment=assignment,
            runner=stage,
            next_stage=plan.next_of(assignment.stage_index),
            return_to=plan.stages[0],
        )


class Pipeline:
    """The host: plans the split, brings every machine up, and runs requests through them."""

    def __init__(
        self,
        model_dir: str,
        node_id: str = "host",
        host: str = "127.0.0.1",
        port: int = 8749,
        margin_bytes: int = DEFAULT_MARGIN_BYTES,
    ) -> None:
        self._model_dir = model_dir
        self._margin = margin_bytes
        self._me = local_node_info(node_id, host, port, label="this machine")

        self._server = StageServer(node=self._me)
        self._server.on_token(self._token_arrived)

        self._stage: PartialStage | None = None
        self._plan: StagePlan | None = None

        # One queue per request in flight, holding whatever comes back from the end of the
        # pipeline: a token, or the reason there will not be one.
        self._waiting: dict[str, asyncio.Queue[Frame]] = {}

    @property
    def plan(self) -> StagePlan:
        if self._plan is None:
            raise PipelineError("Nothing has been planned yet.")

        return self._plan

    @property
    def config(self) -> Any:
        """The model's config, which the API needs for its stop tokens and its context length."""
        if self._stage is None:
            raise PipelineError("Nothing has been loaded yet.")

        return self._stage.config

    async def start(self, peers: list[NodeInfo]) -> StagePlan:
        """Brings the whole pipeline up and returns the plan it settled on.

        Raises:
            PipelineError: the machines could not hold the model, or one of them failed to load.
        """
        await self._server.start()

        offered = [self._me, *await self._probe(peers)]

        _log.info("planning across %d machine(s)", len(offered))

        for node in offered:
            _log.info("  %s at %s: %.2f GB free on %s",
                      node.display_name, node.address, node.vram_bytes / 1024 ** 3, node.device)

        try:
            layer_map = build_layer_map(self._model_dir)
            self._plan = planner.plan(layer_map, offered, margin_bytes=self._margin)
        except (PlanError, OSError) as error:
            await self._server.stop()
            raise PipelineError(str(error)) from error

        _log.info("\n%s", self._plan.describe())

        try:
            await self._bring_up()
        except PipelineError:
            await self._server.stop()
            raise

        return self._plan

    async def stop(self) -> None:
        await self._server.stop()

    async def _probe(self, peers: list[NodeInfo]) -> list[NodeInfo]:
        """Asks each machine what it has, and takes its word for it.

        A machine that does not answer is left out of the plan rather than being treated as an
        error. Planning around who turned up is the whole point, and a machine that is not there
        when the pipeline is built would not be there when it ran either.
        """
        found: list[NodeInfo] = []

        for peer in peers:
            try:
                reader, writer = await protocol.connect(peer.host, peer.port,
                                                        attempts=3, pause=1.0)
            except protocol.Disconnected as error:
                _log.warning("%s did not answer and is left out: %s", peer.address, error)
                continue

            try:
                await protocol.write(writer, Frame(protocol.HELLO, {}))
                answer = await asyncio.wait_for(protocol.read(reader), timeout=30.0)

                if answer.kind != protocol.WELCOME:
                    _log.warning("%s answered a hello with %s and is left out",
                                 peer.address, answer.kind)
                    continue

                reported = NodeInfo.from_dict(answer.body)

                # A peer describes its own memory and its own device. Where it lives is what we
                # dialled, because a machine behind a router does not know the address that
                # reached it.
                found.append(NodeInfo(
                    node_id=reported.node_id,
                    host=peer.host,
                    port=peer.port,
                    vram_bytes=reported.vram_bytes,
                    device=reported.device,
                    label=reported.label,
                ))
            except (asyncio.TimeoutError, protocol.ProtocolError, PlanError) as error:
                _log.warning("%s could not be read and is left out: %s", peer.address, error)
            finally:
                writer.close()

                with contextlib.suppress(ConnectionError, OSError):
                    await writer.wait_closed()

        return found

    async def _bring_up(self) -> None:
        """Loads this machine's stage and tells every other machine to load its own."""
        plan = self.plan
        mine = plan.for_node(self._me.node_id)

        others = [stage for stage in plan.stages if stage.node_id != self._me.node_id]

        # The remote machines are told first and load while this one does. Loading is minutes of
        # reading from disk, and doing them one after another would take as long as all of them
        # added together for no reason.
        sending = [asyncio.create_task(self._assign(stage)) for stage in others]

        self._stage = PartialStage(plan.model_dir, mine)

        try:
            await asyncio.to_thread(self._stage.load)
        except Exception as error:  # noqa: BLE001 - the caller wants one kind of failure
            for task in sending:
                task.cancel()

            raise PipelineError(f"This machine could not load its own stage: {error}") from error

        self._server.attach(
            assignment=mine,
            runner=self._stage,
            next_stage=plan.next_of(mine.stage_index),
            return_to=None,
        )

        failures = [result for result in await asyncio.gather(*sending, return_exceptions=True)
                    if result is not None]

        if failures:
            raise PipelineError(
                "The pipeline could not be assembled. " + " ".join(str(f) for f in failures)
            )

        _log.info("every stage is loaded, %d in the pipeline", plan.stage_count)

    async def _assign(self, stage: StageAssignment) -> str | None:
        """Sends one machine its share and waits for it to say whether it took it."""
        try:
            reader, writer = await protocol.connect(stage.host, stage.port, attempts=5)
        except protocol.Disconnected as error:
            return f"{stage.node_id} could not be reached: {error}"

        try:
            await protocol.write(writer, Frame(protocol.LOAD, {"plan": self.plan.to_dict()}))

            # No timeout. A stage holding thirty layers reads tens of gigabytes off a disk, and
            # there is no honest number of seconds that is long enough for a slow machine and
            # short enough to be a useful check on a broken one. A machine that has died is
            # noticed by its socket closing, which arrives immediately.
            answer = await protocol.read(reader)

            if answer.kind == protocol.ERROR:
                return f"{stage.node_id} refused its assignment: {answer.body.get('reason')}"

            if answer.kind != protocol.LOADED or not answer.body.get("loaded"):
                return f"{stage.node_id} did not load: {answer.body.get('reason', answer.kind)}"

            _log.info("%s loaded %s", stage.node_id, answer.body.get("holding", ""))

            return None
        except protocol.ProtocolError as error:
            return f"{stage.node_id} went away while loading: {error}"
        finally:
            writer.close()

            with contextlib.suppress(ConnectionError, OSError):
                await writer.wait_closed()

    def _token_arrived(self, frame: Frame) -> None:
        """Whatever came back from the end of the pipeline, handed to the request waiting on it.

        A failure with no request named belongs to every request in flight, because that is what
        a broken pipeline is: nothing that is running can finish.
        """
        request_id = str(frame.body.get("request_id", ""))
        waiting = self._waiting.get(request_id)

        if waiting is not None:
            waiting.put_nowait(frame)
            return

        if frame.kind == protocol.ERROR:
            for queue in self._waiting.values():
                queue.put_nowait(frame)

            return

        _log.warning("a token came back for %s, which nothing is waiting for", request_id)

    async def generate(
        self,
        prompt_tokens: list[int],
        sampling: dict[str, Any],
        max_new_tokens: int,
        stop_tokens: set[int],
    ):
        """Runs one request through the pipeline, yielding each token as it comes back.

        The prompt goes through once as a batch, and after that each token goes round the whole
        pipeline on its own. That is what pipeline parallelism costs: a stage boundary is paid
        once per token rather than once per request.

        Raises:
            PipelineError: a stage failed, or nothing came back in time.
        """
        request_id = uuid.uuid4().hex
        queue: asyncio.Queue[Frame] = asyncio.Queue()
        self._waiting[request_id] = queue

        # The history goes with the first frame only. The stage that samples keeps it and adds to
        # it, so sending it again every step would put the whole conversation on the wire once
        # per token.
        first = dict(sampling)
        first["history"] = list(prompt_tokens)

        try:
            carried = torch.tensor([prompt_tokens], dtype=torch.int64)
            offset = 0
            settings = first

            for _ in range(max_new_tokens):
                try:
                    await self._push(request_id, carried, offset, settings)
                except protocol.Disconnected as error:
                    # The machine after this one is not there. That is the end of the request,
                    # and saying so is the whole difference between a failure and a hang.
                    raise PipelineError(
                        f"The next machine in the pipeline could not be reached: {error}"
                    ) from error

                frame = await self._await_token(queue, request_id)
                token = int(frame.body["token_id"])

                yield token

                if token in stop_tokens:
                    return

                offset += carried.shape[1]
                carried = torch.tensor([[token]], dtype=torch.int64)
                settings = sampling
        finally:
            self._waiting.pop(request_id, None)

            with contextlib.suppress(Exception):
                await self._release(request_id)

    async def _push(
        self,
        request_id: str,
        tokens: torch.Tensor,
        offset: int,
        sampling: dict[str, Any],
    ) -> None:
        """Runs stage 0 here and sends what it produced to the next machine."""
        produced = await self._server.run_step(request_id, tokens, offset, sampling)

        body = {"request_id": request_id, "position_offset": offset, "sampling": sampling}

        if self.plan.stage_count == 1:
            # One stage means this machine is also the last one, so what came back is a token
            # rather than hidden states and there is nowhere to send it.
            self._token_arrived(Frame(protocol.TOKEN, {
                "request_id": request_id,
                "token_id": int(produced.reshape(-1)[0].item()),
            }))
            return

        await self._server.send_onward(Frame(protocol.FORWARD, body, produced))

    async def _await_token(self, queue: asyncio.Queue[Frame], request_id: str) -> Frame:
        try:
            frame = await asyncio.wait_for(queue.get(), timeout=TOKEN_TIMEOUT_SECONDS)
        except asyncio.TimeoutError as error:
            raise PipelineError(
                f"No token came back within {TOKEN_TIMEOUT_SECONDS:.0f} seconds. "
                "A machine in the pipeline has stopped answering."
            ) from error

        if frame.kind == protocol.ERROR:
            raise PipelineError(str(frame.body.get("reason", "a stage failed")))

        return frame

    async def _release(self, request_id: str) -> None:
        """Tells every stage to drop what it held for a request, starting with this one."""
        self._server.release(request_id)

        if self.plan.stage_count > 1:
            await self._server.send_onward(
                Frame(protocol.RELEASE, {"request_id": request_id})
            )
