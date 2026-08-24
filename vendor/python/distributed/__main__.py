"""Starting a machine as one end of a pipeline or the other.

    python -m distributed peer --host 0.0.0.0 --port 8749
    python -m distributed host --model <folder> --peer 192.168.1.20:8749 --api-port 8750

The application starts these the way it starts every other engine: as a child process, in a job
object, spoken to over HTTP once it is up. There is no import path from the application into this
package and there should not be, because a model that takes minutes to load and gigabytes to hold
does not belong in the process drawing the window.

A peer is started first and stays up. It holds nothing until a host tells it what to hold, which
is what makes a contributing machine a machine somebody leaves running rather than one they
coordinate by hand.
"""

from __future__ import annotations

import argparse
import asyncio
import logging
import socket
import sys

import uvicorn

from .activation_server import local_node_info
from .api import create_app
from .config import (
    DEFAULT_API_PORT,
    DEFAULT_MARGIN_BYTES,
    DEFAULT_STAGE_PORT,
    QUANTIZATION_NONE,
    QUANTIZATIONS,
    NodeInfo,
)
from .coordinator import Peer, Pipeline, PipelineError


def _arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(prog="distributed", description=__doc__)
    roles = parser.add_subparsers(dest="role", required=True)

    peer = roles.add_parser("peer", help="contribute this machine to somebody else's pipeline")
    peer.add_argument("--host", default="0.0.0.0",
                      help="the address to listen on. 0.0.0.0 accepts from the network.")
    peer.add_argument("--port", type=int, default=DEFAULT_STAGE_PORT)
    peer.add_argument("--node-id", default="",
                      help="a stable name for this machine. Defaults to its hostname.")

    host = roles.add_parser("host", help="plan a pipeline and answer requests for it")
    host.add_argument("--model", required=True, help="the safetensors model folder")
    host.add_argument("--host", default="127.0.0.1",
                      help="the address this machine's own stage listens on")
    host.add_argument("--port", type=int, default=DEFAULT_STAGE_PORT)
    host.add_argument("--api-host", default="127.0.0.1")
    host.add_argument("--api-port", type=int, default=DEFAULT_API_PORT)
    host.add_argument("--peer", action="append", default=[], metavar="HOST:PORT",
                      help="a machine to plan across. Repeat for each one.")
    host.add_argument("--margin-gb", type=float, default=DEFAULT_MARGIN_BYTES / 1024 ** 3,
                      help="memory held back on every machine for the cache and the run")
    host.add_argument("--node-id", default="host")
    host.add_argument("--quantize", default=QUANTIZATION_NONE, choices=list(QUANTIZATIONS),
                      help="how every machine loads its weights. 4bit needs bitsandbytes, and "
                           "falls back to full precision with a warning if it is not installed.")

    parser.add_argument("--log-level", default="info")

    return parser.parse_args()


def _peer_from(text: str) -> NodeInfo:
    """An address somebody typed, as a machine to ask.

    Nothing is assumed about what is there. The memory reported here is zero because the machine
    has not been asked yet, and asking is the coordinator's first move.
    """
    host, _, port = text.rpartition(":")

    if not host:
        host, port = text, str(DEFAULT_STAGE_PORT)

    try:
        return NodeInfo(node_id=text, host=host, port=int(port))
    except ValueError:
        raise SystemExit(f"{text!r} is not a host:port address.") from None


async def _run_peer(options: argparse.Namespace) -> int:
    node = local_node_info(
        node_id=options.node_id or socket.gethostname(),
        host=options.host,
        port=options.port,
    )

    peer = Peer(node)
    await peer.start()

    logging.info("%s is offering %.2f GB on %s and is waiting to be given layers",
                 node.display_name, node.vram_bytes / 1024 ** 3, node.device)

    try:
        await asyncio.Event().wait()
    except (KeyboardInterrupt, asyncio.CancelledError):
        pass
    finally:
        await peer.stop()

    return 0


async def _run_host(options: argparse.Namespace) -> int:
    pipeline = Pipeline(
        model_dir=options.model,
        node_id=options.node_id,
        host=options.host,
        port=options.port,
        margin_bytes=int(options.margin_gb * 1024 ** 3),
        quantization=options.quantize,
    )

    try:
        plan = await pipeline.start([_peer_from(text) for text in options.peer])
    except PipelineError as error:
        logging.error("%s", error)
        return 1

    print(plan.describe(), flush=True)

    app = create_app(pipeline, options.model)

    server = uvicorn.Server(uvicorn.Config(
        app,
        host=options.api_host,
        port=options.api_port,
        log_level=options.log_level,
        access_log=False,
    ))

    try:
        await server.serve()
    finally:
        await pipeline.stop()

    return 0


def main() -> int:
    options = _arguments()

    logging.basicConfig(
        level=getattr(logging, options.log_level.upper(), logging.INFO),
        format="%(asctime)s %(levelname)-7s %(name)s: %(message)s",
        datefmt="%H:%M:%S",
    )

    runner = _run_peer if options.role == "peer" else _run_host

    try:
        return asyncio.run(runner(options))
    except KeyboardInterrupt:
        return 0


if __name__ == "__main__":
    sys.exit(main())
