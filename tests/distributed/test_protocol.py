"""The wire format, including what it does with frames that are not frames.

Two things are being protected here. That a tensor survives the trip unchanged, because a
pipeline that quietly alters activations produces a model that is subtly wrong rather than
visibly broken. And that a malformed frame is refused rather than acted on, because this socket
is reachable from another machine.
"""
from __future__ import annotations

import asyncio
import json

import pytest
import torch

from distributed import protocol
from distributed.protocol import Frame

pytestmark = pytest.mark.anyio if False else []


def _round_trip(frame: Frame) -> Frame:
    """Encodes a frame and reads it back through a real stream reader."""

    async def go() -> Frame:
        reader = asyncio.StreamReader()
        reader.feed_data(protocol.encode(frame))
        reader.feed_eof()

        return await protocol.read(reader)

    return asyncio.run(go())


class TestTensors:
    """Values, dtypes and shapes come back exactly as they went."""

    @pytest.mark.parametrize("dtype", [torch.bfloat16, torch.float16, torch.float32, torch.int64])
    def test_a_tensor_survives_unchanged(self, dtype):
        original = (torch.randn(2, 5, 64) * 10).to(dtype)

        back = _round_trip(Frame(protocol.FORWARD, {"request_id": "r"}, original)).tensor

        assert back is not None
        assert back.dtype == original.dtype
        assert back.shape == original.shape
        assert torch.equal(back, original)

    def test_bfloat16_is_exact(self):
        """Called out separately because it has no numpy equivalent and takes its own path."""
        original = torch.randn(1, 7, 2048, dtype=torch.bfloat16)

        back = _round_trip(Frame(protocol.FORWARD, {}, original)).tensor

        assert torch.equal(back, original)

    def test_an_empty_tensor_survives(self):
        original = torch.empty((1, 0, 2048), dtype=torch.bfloat16)

        back = _round_trip(Frame(protocol.FORWARD, {}, original)).tensor

        assert back.shape == original.shape

    def test_a_zero_dimensional_tensor_survives(self):
        """A scalar cannot be reinterpreted as a narrower type until it has a dimension.

        Torch refuses ``view(torch.uint8)`` on a zero dimensional tensor outright, so encoding
        one used to raise rather than produce a frame. Flattening first is what fixes it.
        """
        back = _round_trip(Frame(protocol.FORWARD, {}, torch.tensor(3.5))).tensor

        assert back is not None
        assert back.reshape(-1).item() == pytest.approx(3.5)

    def test_a_frame_with_no_tensor_stays_that_way(self):
        back = _round_trip(Frame(protocol.TOKEN, {"request_id": "r", "token_id": 7}))

        assert back.tensor is None
        assert back.body == {"request_id": "r", "token_id": 7}


class TestMalformedFrames:
    """Anything that is not a frame is refused, and refused with a reason."""

    def _read(self, raw: bytes) -> None:
        async def go() -> None:
            reader = asyncio.StreamReader()
            reader.feed_data(raw)
            reader.feed_eof()
            await protocol.read(reader)

        asyncio.run(go())

    def test_a_header_larger_than_the_cap_is_refused(self):
        with pytest.raises(protocol.ProtocolError):
            self._read((2 ** 30).to_bytes(4, "big"))

    def test_a_header_that_is_not_json_is_refused(self):
        with pytest.raises(protocol.ProtocolError):
            self._read((4).to_bytes(4, "big") + b"\xff\xff\xff\xff")

    def test_a_header_with_no_kind_is_refused(self):
        head = json.dumps({"body": {}}).encode()

        with pytest.raises(protocol.ProtocolError):
            self._read(len(head).to_bytes(4, "big") + head)

    def test_a_payload_disagreeing_with_the_shape_is_refused(self):
        head = json.dumps({
            "kind": "forward", "body": {},
            "tensor": {"dtype": "bfloat16", "shape": [1, 4, 2048], "nbytes": 16},
        }).encode()

        with pytest.raises(protocol.ProtocolError):
            self._read(len(head).to_bytes(4, "big") + head + b"\x00" * 16)

    def test_a_dtype_that_is_not_a_dtype_is_refused(self):
        """The name arrives over a socket, so it must not be fetched off torch and called."""
        head = json.dumps({
            "kind": "forward", "body": {},
            "tensor": {"dtype": "load", "shape": [1], "nbytes": 4},
        }).encode()

        with pytest.raises(protocol.ProtocolError):
            self._read(len(head).to_bytes(4, "big") + head + b"\x00" * 4)

    def test_a_truncated_frame_is_refused(self):
        whole = protocol.encode(Frame(protocol.FORWARD, {}, torch.zeros(1, 2, 8)))

        with pytest.raises(protocol.ProtocolError):
            self._read(whole[:-10])


class TestHandshake:
    """The shared secret, which is the whole of what keeps a stranger off a peer."""

    def test_the_right_secret_is_accepted_and_the_wrong_one_is_not(self):
        async def go() -> tuple[bool, bool]:
            served: list[bool] = []

            async def handle(reader, writer):
                # Mirrors what StageServer does, which matters for the refusal: sending the
                # reason back is what turns "the machine hung up on me" into "your secret is
                # wrong", and a handler that just closed would test the wrong contract.
                try:
                    await protocol.accept_handshake(reader, writer, "shared")
                    served.append(True)

                    # Held open briefly, because closing the instant a handshake is accepted
                    # races the client's read of the acceptance. In the real server the
                    # connection goes on to carry frames, so this never arises there.
                    await asyncio.sleep(0.5)
                except protocol.NotAuthorised as refusal:
                    served.append(False)
                    await protocol.write(writer, protocol.error_frame(str(refusal)))
                    await asyncio.sleep(0.2)
                finally:
                    writer.close()

            server = await asyncio.start_server(handle, "127.0.0.1", 0)
            port = server.sockets[0].getsockname()[1]

            good = True
            try:
                _, writer = await protocol.connect("127.0.0.1", port, attempts=1, secret="shared")
                writer.close()
            except protocol.NotAuthorised:
                good = False

            await asyncio.sleep(0.1)

            bad = False
            try:
                _, writer = await protocol.connect("127.0.0.1", port, attempts=1, secret="wrong")
                writer.close()
                bad = True
            except protocol.NotAuthorised:
                bad = False

            await asyncio.sleep(0.1)
            server.close()
            await server.wait_closed()

            return good, bad

        accepted, wrong_accepted = asyncio.run(go())

        assert accepted, "the correct secret was refused"
        assert not wrong_accepted, "the wrong secret was accepted"

    def test_a_proof_cannot_be_replayed(self):
        """Signing a nonce the accepting end chose is what makes a recording useless."""
        first = protocol._proof("shared", "nonce-one")
        second = protocol._proof("shared", "nonce-two")

        assert first != second

    def test_a_secret_is_read_from_the_environment(self, monkeypatch):
        monkeypatch.setenv(protocol.SECRET_ENVIRONMENT_VARIABLE, "  from-environment  ")

        assert protocol.resolve_secret(None) == "from-environment"
        assert protocol.resolve_secret("explicit") == "explicit"

    def test_no_secret_anywhere_is_none(self, monkeypatch):
        monkeypatch.delenv(protocol.SECRET_ENVIRONMENT_VARIABLE, raising=False)

        assert protocol.resolve_secret(None) is None
        assert protocol.resolve_secret("   ") is None
