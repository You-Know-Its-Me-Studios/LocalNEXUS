"""How one stage talks to the next.

A frame is three pieces: four bytes saying how long the header is, a JSON header, and raw tensor
bytes whose length the header states. That is the whole format. It is JSON rather than msgpack
because msgpack is not in the locked environment and adding it would mean every machine
re-provisioning a multi gigabyte Python install to save a few hundred bytes per frame on a header
that is already dwarfed by the tensor behind it.

The tensor bytes are the tensor's own memory, copied once and not converted. A hidden state for
this model is 2048 values wide, so a single decode step is four kilobytes and a prompt of a
thousand tokens is four megabytes. Latency is what costs, not bandwidth, which is why the socket
is left open between stages rather than reconnected per request.

Nothing here knows what a stage does with a frame. This module moves bytes and turns them back
into tensors, and that is all it does.
"""

from __future__ import annotations

import asyncio
import hashlib
import hmac
import json
import os
import secrets
import struct
from dataclasses import dataclass, field
from typing import Any

import torch

#: Four bytes, big endian, unsigned: the length of the JSON header that follows.
_LENGTH = struct.Struct(">I")

#: A header describes a frame and is never large. Anything claiming more than this is not one of
#: ours, and honouring it would mean allocating whatever a stranger asked for.
_MAX_HEADER_BYTES = 1 * 1024 * 1024

#: A prompt's worth of hidden states is megabytes. A gigabyte is not a frame, it is a fault or an
#: attack, and either way refusing is better than trying to hold it.
_MAX_PAYLOAD_BYTES = 1 * 1024 ** 3

# The frame kinds. Every one of these is a noun or a plain verb, because these strings end up in
# logs that somebody has to read at the point where something has already gone wrong.

#: Coordinator to stage: here is your assignment, load it.
LOAD = "load"

#: Stage to coordinator: I have loaded, or I have not and here is why.
LOADED = "loaded"

#: Coordinator to stage, or anyone to anyone: what are you and what do you have.
HELLO = "hello"

#: The answer to hello: node id, device, free memory.
WELCOME = "welcome"

#: Stage to the next stage: hidden states for one request, one step.
FORWARD = "forward"

#: Last stage to the host: the token that came out, or the reason there is not one.
TOKEN = "token"

#: Anyone to a stage: this request is over, drop its cache.
RELEASE = "release"

#: The answer to anything that failed, carrying the reason rather than closing the socket.
ERROR = "error"

#: Sent by whichever end accepted the connection, carrying a nonce to be signed.
CHALLENGE = "challenge"

#: The answer to a challenge: proof that this end knows the shared secret.
AUTH = "auth"

#: Sent when a proof was accepted. Nothing else is dispatched until this has gone out.
AUTH_OK = "auth_ok"

#: The environment variable a secret is read from when it is not passed on the command line.
#: An environment variable rather than an argument is what keeps the secret out of the process
#: list, where every other user on the machine can read it.
SECRET_ENVIRONMENT_VARIABLE = "LOCALNEXUS_DISTRIBUTED_SECRET"

#: How long the other end has to complete the handshake. A connection that opens and then says
#: nothing is either broken or someone counting how many sockets this will hold open at once,
#: and both are answered by hanging up.
HANDSHAKE_TIMEOUT_SECONDS = 15.0

#: Bytes of randomness in a challenge. A nonce is only required to not repeat.
_NONCE_BYTES = 32


class ProtocolError(Exception):
    """A frame could not be read, or was not a frame at all."""


class Disconnected(ProtocolError):
    """The other end went away. Expected on shutdown, and a fault at any other time."""


@dataclass(frozen=True)
class Frame:
    """One message: what kind it is, what it says, and optionally a tensor."""

    kind: str
    body: dict[str, Any] = field(default_factory=dict)
    tensor: torch.Tensor | None = None

    def __repr__(self) -> str:
        shape = tuple(self.tensor.shape) if self.tensor is not None else None

        return f"Frame({self.kind}, body={self.body}, tensor={shape})"


def encode(frame: Frame) -> bytes:
    """One frame as the bytes that go on the wire."""
    header: dict[str, Any] = {"kind": frame.kind, "body": frame.body}
    payload = b""

    if frame.tensor is not None:
        payload = _to_bytes(frame.tensor)
        header["tensor"] = {
            "dtype": _dtype_name(frame.tensor.dtype),
            "shape": list(frame.tensor.shape),
            "nbytes": len(payload),
        }

    raw = json.dumps(header, separators=(",", ":")).encode("utf-8")

    if len(raw) > _MAX_HEADER_BYTES:
        raise ProtocolError(f"A {len(raw)} byte header is too large to send.")

    return _LENGTH.pack(len(raw)) + raw + payload


async def write(writer: asyncio.StreamWriter, frame: Frame) -> None:
    """Sends one frame and waits for it to be handed to the socket.

    The drain is not optional. Without it a stage that produces frames faster than the network
    takes them buffers the difference in memory, on a machine chosen for having none to spare.
    """
    writer.write(encode(frame))
    await writer.drain()


async def read(reader: asyncio.StreamReader, device: str | torch.device = "cpu") -> Frame:
    """Reads one frame, putting any tensor it carries straight onto ``device``.

    Raises:
        Disconnected: the other end closed, cleanly or otherwise.
        ProtocolError: what arrived is not a frame this can read.
    """
    prefix = await _exactly(reader, _LENGTH.size)
    (length,) = _LENGTH.unpack(prefix)

    if length == 0 or length > _MAX_HEADER_BYTES:
        raise ProtocolError(f"A frame declared a {length} byte header, which is not a frame.")

    try:
        header = json.loads((await _exactly(reader, length)).decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ProtocolError(f"A frame header could not be read: {error}") from error

    if not isinstance(header, dict) or "kind" not in header:
        raise ProtocolError("A frame header has no kind, so there is no telling what it is.")

    body = header.get("body")
    described = header.get("tensor")
    tensor = None

    if described is not None:
        tensor = await _read_tensor(reader, described, device)

    return Frame(
        kind=str(header["kind"]),
        body=body if isinstance(body, dict) else {},
        tensor=tensor,
    )


async def _read_tensor(
    reader: asyncio.StreamReader,
    described: Any,
    device: str | torch.device,
) -> torch.Tensor:
    if not isinstance(described, dict):
        raise ProtocolError("A frame describes a tensor with something that is not a description.")

    try:
        nbytes = int(described["nbytes"])
        shape = tuple(int(dimension) for dimension in described["shape"])
        dtype = _dtype_from_name(str(described["dtype"]))
    except (KeyError, TypeError, ValueError) as error:
        raise ProtocolError(f"A tensor description could not be read: {error}") from error

    if nbytes < 0 or nbytes > _MAX_PAYLOAD_BYTES:
        raise ProtocolError(f"A frame declared a {nbytes} byte tensor, which is not one.")

    expected = _element_count(shape) * dtype.itemsize

    if nbytes != expected:
        raise ProtocolError(
            f"A tensor of shape {shape} at {dtype} is {expected} bytes and the frame says "
            f"{nbytes}. The header and the payload do not agree."
        )

    raw = await _exactly(reader, nbytes)

    return _from_bytes(raw, dtype, shape).to(device)


async def _exactly(reader: asyncio.StreamReader, count: int) -> bytes:
    """Reads exactly ``count`` bytes, because a socket read returns whatever has arrived.

    ``readexactly`` already does this. It is wrapped so that the end of a connection is one
    exception type here rather than two that callers have to know about.
    """
    if count == 0:
        return b""

    try:
        return await reader.readexactly(count)
    except asyncio.IncompleteReadError as error:
        raise Disconnected(
            f"The connection ended after {len(error.partial)} of {count} bytes."
        ) from error
    except (ConnectionError, OSError) as error:
        raise Disconnected(f"The connection failed: {error}") from error


def _to_bytes(tensor: torch.Tensor) -> bytes:
    """A tensor's memory, exactly as it is laid out.

    The reinterpret through uint8 is what makes this work for bfloat16, which has no numpy dtype
    to convert to and so cannot go the usual route. Nothing is converted and nothing is scaled:
    the bytes that come out are the bytes that were in the tensor.
    """
    settled = tensor.detach().to("cpu", copy=False).contiguous()

    return settled.view(torch.uint8).numpy().tobytes()


def _from_bytes(raw: bytes, dtype: torch.dtype, shape: tuple[int, ...]) -> torch.Tensor:
    """The tensor those bytes were.

    ``frombuffer`` wants memory it is allowed to write to, and the bytes read off a socket are
    immutable, so they are copied into a bytearray first. That copy is the only one on this path.
    """
    if _element_count(shape) == 0:
        return torch.empty(shape, dtype=dtype)

    flat = torch.frombuffer(bytearray(raw), dtype=torch.uint8)

    return flat.view(dtype).reshape(shape)


def _element_count(shape: tuple[int, ...]) -> int:
    count = 1

    for dimension in shape:
        if dimension < 0:
            raise ProtocolError(f"A tensor shape {shape} has a negative dimension.")

        count *= dimension

    return count


def _dtype_name(dtype: torch.dtype) -> str:
    """torch.bfloat16 as "bfloat16", which is what goes in the header."""
    return str(dtype).removeprefix("torch.")


def _dtype_from_name(name: str) -> torch.dtype:
    """The reverse, refusing anything that is not a dtype rather than fetching an attribute.

    ``getattr(torch, name)`` on a name that arrived over a socket will happily return a function,
    so what comes back is checked for being a dtype before it is used as one.
    """
    found = getattr(torch, name, None)

    if not isinstance(found, torch.dtype):
        raise ProtocolError(f"{name!r} is not a dtype this understands.")

    return found


async def connect(
    host: str,
    port: int,
    timeout: float = 30.0,
    attempts: int = 30,
    pause: float = 1.0,
    secret: str | None = None,
) -> tuple[asyncio.StreamReader, asyncio.StreamWriter]:
    """Opens a connection to a stage, waiting for it to come up, and proves who it is.

    Stages are started at roughly the same time and a stage holding forty layers takes minutes to
    load, so the one that is ready first will always find the next one absent. Retrying is the
    normal path here rather than the error path, which is why the wait is generous.

    The handshake happens here rather than at the call sites so that there is no way to obtain a
    usable connection without having passed it. A refused proof is not retried: the secret is not
    going to become right on the second attempt, and retrying it thirty times against a machine
    that already said no is how a wrong secret becomes a lockout.

    Raises:
        Disconnected: the stage never answered.
        NotAuthorised: it answered and would not accept this machine.
    """
    last: Exception | None = None

    for attempt in range(attempts):
        try:
            reader, writer = await asyncio.wait_for(
                asyncio.open_connection(host, port), timeout)
        except (ConnectionError, OSError, asyncio.TimeoutError) as error:
            last = error

            if attempt + 1 < attempts:
                await asyncio.sleep(pause)

            continue

        try:
            await offer_handshake(reader, writer, secret)
        except NotAuthorised:
            writer.close()
            raise
        except (ProtocolError, ConnectionError, OSError, asyncio.TimeoutError) as error:
            writer.close()
            last = error

            if attempt + 1 < attempts:
                await asyncio.sleep(pause)

            continue

        return reader, writer

    raise Disconnected(
        f"{host}:{port} did not answer after {attempts} attempts over "
        f"{attempts * pause:.0f} seconds. The last reason was: {last}"
    )


class NotAuthorised(ProtocolError):
    """The other end could not prove it knows the shared secret."""


def resolve_secret(explicit: str | None) -> str | None:
    """The shared secret, from the command line or the environment, or nothing.

    The environment is preferred for the real deployment because a command line argument is
    visible in the process list to every account on the machine, which is a poor place for the
    one thing standing between a stranger and this machine's GPU.
    """
    if explicit is not None and explicit.strip():
        return explicit.strip()

    fromenvironment = os.environ.get(SECRET_ENVIRONMENT_VARIABLE, "").strip()

    return fromenvironment or None


def _proof(secret: str, nonce: str) -> str:
    """A signature over the challenge, which is what actually crosses the wire.

    The secret itself is never sent. Signing a nonce that the accepting end chose means a
    recording of one handshake cannot be replayed into the next, which a bare shared password
    would not survive on a network anybody can listen to.
    """
    return hmac.new(secret.encode("utf-8"), nonce.encode("utf-8"), hashlib.sha256).hexdigest()


async def accept_handshake(
    reader: asyncio.StreamReader,
    writer: asyncio.StreamWriter,
    secret: str | None,
) -> None:
    """Proves, from the accepting side, that whoever connected knows the secret.

    Called before anything else is read from a connection. A stage with no secret configured is
    loopback only by the time it gets here, which the entry point enforces, so an absent secret
    means the operator deliberately chose a private pipeline on one machine.

    Raises:
        NotAuthorised: the proof was wrong, absent, or too slow in coming.
    """
    if secret is None:
        return

    nonce = secrets.token_hex(_NONCE_BYTES)

    await write(writer, Frame(CHALLENGE, {"nonce": nonce}))

    try:
        answer = await asyncio.wait_for(read(reader), timeout=HANDSHAKE_TIMEOUT_SECONDS)
    except asyncio.TimeoutError as error:
        raise NotAuthorised(
            f"the other end did not answer the challenge within "
            f"{HANDSHAKE_TIMEOUT_SECONDS:.0f} seconds"
        ) from error

    if answer.kind != AUTH:
        raise NotAuthorised(f"the other end sent {answer.kind!r} instead of proving who it is")

    offered = str(answer.body.get("proof", ""))

    # Constant time, so that failing does not leak how much of the proof was right.
    if not hmac.compare_digest(offered, _proof(secret, nonce)):
        raise NotAuthorised("the other end does not know the shared secret")

    await write(writer, Frame(AUTH_OK, {}))


async def offer_handshake(
    reader: asyncio.StreamReader,
    writer: asyncio.StreamWriter,
    secret: str | None,
) -> None:
    """Answers a challenge from the connecting side.

    Tolerant of an end that does not challenge, because a loopback pipeline with no secret is a
    supported arrangement and this same function serves both.

    Raises:
        NotAuthorised: the other end refused the proof, or wanted one that cannot be given.
    """
    # Nothing configured here means nothing to prove. An end that does want proof will see an
    # ordinary frame arrive where an answer should have been, and hang up, which is the outcome
    # that protects it. Returning rather than waiting keeps a loopback pipeline from paying the
    # handshake timeout on every connection.
    if secret is None:
        return

    try:
        first = await asyncio.wait_for(read(reader), timeout=HANDSHAKE_TIMEOUT_SECONDS)
    except asyncio.TimeoutError as error:
        raise NotAuthorised(
            f"{HANDSHAKE_TIMEOUT_SECONDS:.0f} seconds passed with nothing from the other end"
        ) from error

    if first.kind != CHALLENGE:
        raise NotAuthorised(
            f"expected a challenge and got {first.kind!r}. The other end is not this protocol."
        )

    if secret is None:
        raise NotAuthorised(
            "the other end asked for a shared secret and none is configured here. Give this "
            f"machine the same --secret, or set {SECRET_ENVIRONMENT_VARIABLE}."
        )

    await write(writer, Frame(AUTH, {"proof": _proof(secret, str(first.body.get("nonce", "")))}))

    settled = await asyncio.wait_for(read(reader), timeout=HANDSHAKE_TIMEOUT_SECONDS)

    if settled.kind == ERROR:
        raise NotAuthorised(str(settled.body.get("reason", "the other end refused this machine")))

    if settled.kind != AUTH_OK:
        raise NotAuthorised(f"the other end answered a proof with {settled.kind!r}")


def error_frame(reason: str, **details: Any) -> Frame:
    """A refusal that stays on the socket, so the other end learns why rather than guessing.

    Closing the connection is also an answer and it is a much worse one: the other end sees only
    that the machine went away, which is the same thing it sees when a machine goes away.
    """
    return Frame(kind=ERROR, body={"reason": reason, **details})
