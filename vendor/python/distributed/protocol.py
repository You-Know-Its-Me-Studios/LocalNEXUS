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
import json
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
) -> tuple[asyncio.StreamReader, asyncio.StreamWriter]:
    """Opens a connection to a stage, waiting for it to come up.

    Stages are started at roughly the same time and a stage holding forty layers takes minutes to
    load, so the one that is ready first will always find the next one absent. Retrying is the
    normal path here rather than the error path, which is why the wait is generous.

    Raises:
        Disconnected: the stage never answered.
    """
    last: Exception | None = None

    for attempt in range(attempts):
        try:
            return await asyncio.wait_for(asyncio.open_connection(host, port), timeout)
        except (ConnectionError, OSError, asyncio.TimeoutError) as error:
            last = error

            if attempt + 1 < attempts:
                await asyncio.sleep(pause)

    raise Disconnected(
        f"{host}:{port} did not answer after {attempts} attempts over "
        f"{attempts * pause:.0f} seconds. The last reason was: {last}"
    )


def error_frame(reason: str, **details: Any) -> Frame:
    """A refusal that stays on the socket, so the other end learns why rather than guessing.

    Closing the connection is also an answer and it is a much worse one: the other end sees only
    that the machine went away, which is the same thing it sees when a machine goes away.
    """
    return Frame(kind=ERROR, body={"reason": reason, **details})
