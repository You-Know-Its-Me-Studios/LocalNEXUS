# Splitting a safetensors model across machines

There are two unrelated ways to use more than one machine in LocalNEXUS, and picking the wrong
one wastes an afternoon. This document is one of them.

| You have | Use | Documented in |
| --- | --- | --- |
| A GGUF model, and you want to serve it to other people, or use one somebody else serves | The mesh | [distributed-mesh.md](distributed-mesh.md) |
| A safetensors model too large for one machine, and machines of your own to spread it over | This | here |

They share no code. The mesh is a separate engine that LocalNEXUS starts and reads; this is a
Python package in `vendor/python/distributed` that LocalNEXUS starts as a child process. The mesh
cannot serve safetensors, and this cannot serve GGUF.

## What it does

A model is cut into contiguous ranges of decoder layers, one range per machine. Each machine
loads only its own range. A request walks the machines in order carrying hidden states rather
than tokens: the first machine turns tokens into hidden states, each machine in turn runs its
layers, and the last one applies the final norm and the head and samples the next token.

That last part is worth being clear about, because it decides whether this is worth doing at
all. **Every token crosses every machine.** Splitting does not make generation faster. It makes
a model runnable that otherwise would not run at all, and it costs a network round trip per
token per boundary. If a model fits on one machine, serve it on one machine.

## Turning it on

**Network tab, Distributed inference.**

- **Split large safetensors models across machines.** Off by default. While it is off nothing
  changes: safetensors models are served whole by `transformers serve` exactly as before.
- **Shared secret.** The same value on every machine. Leave it blank only if every machine in
  the pipeline is this one.
- **Machines.** One `host:port` per contributing machine, for example `192.168.1.20:8749`.
  Adding a row here does not start anything. You start the peer yourself, below.

Turning the switch on does not always change which runtime answers. The distributed path is
taken when there is somewhere to distribute to, or when the model does not fit on this machine.
A model that fits, with no machines listed, still goes to the single machine runtime, because a
pipeline of one stage is the same answer reached more slowly.

## Starting a contributing machine

On each machine that will hold layers, from a command line:

```powershell
# Loopback only. Fine when the whole pipeline is one machine.
python -m distributed peer

# Reachable from the network, which requires a secret.
$env:LOCALNEXUS_DISTRIBUTED_SECRET = "the same long random value on every machine"
python -m distributed peer --host 192.168.1.20
```

The interpreter is the one LocalNEXUS built, at
`%LOCALAPPDATA%\LocalNEXUS\runtime\python\.venv\Scripts\python.exe`, and `PYTHONPATH` has to
point at the folder holding the package, which is `vendor\python` beside the application.

A peer refuses to start on an address the network can reach without a secret. That is not
configurable, and it is the whole of what stops a stranger driving your GPU.

The model has to already be on that machine's disk, at a path the host will name. A peer will
not download one: a model directory arriving over a socket is checked to be a real folder before
anything is loaded, so a repository id or a URL is refused.

The host is started by LocalNEXUS itself when a model node asks for a model. You do not run
`python -m distributed host` unless you are working on the package.

## What it looks like when it works

The host plans the split from what each machine reports it has free, then hands each one its
range. `GET /health` on the host's API port describes the result:

```json
{
  "status": "ok",
  "model": "Qwen3-Coder-30B-A3B-Instruct",
  "quantization_planned": "none",
  "quantization_applied": "none",
  "stages": [
    {"stage": 0, "node_id": "host",  "layers": [0, 23],  "holds_embedding": true},
    {"stage": 1, "node_id": "peer1", "layers": [24, 47], "holds_head": true}
  ]
}
```

## What it refuses, and why that is the useful part

**The machines cannot hold the model.** The refusal says by how much:

> These machines cannot hold Qwen3-Coder-30B-A3B-Instruct. Layers 30 to 47 and the head are
> unplaced, which is 21.03 GB with nowhere to go. The model needs 55.69 GB, the machines offer
> 40.00 GB, and 36.00 GB of that is usable once 2.00 GB per machine is held back.

That number is the one worth having. It is the difference between "add a machine" and "add the
right machine".

**A machine drops mid request.** The pipeline stops and the request fails with a 503 naming the
machine. There is no failover: a stage's layers exist on exactly one machine, so losing it means
losing the model. Other requests in flight are only failed if the machine is confirmed gone.

**A machine goes quiet.** A peer that has held a stage for twenty minutes with nothing asked of
it and nobody connected gives the memory back on its own, so a host that crashed does not cost
somebody their card until they notice.

## Four bit weights, and why they may do nothing

There is a `--quantize 4bit` option, and on a mixture-of-experts model it saves almost nothing.
This is worth understanding before relying on it.

Quantization in transformers is a module swap: it walks the model and replaces `nn.Linear`
layers with four bit equivalents. A mixture-of-experts model does not store its experts as
`nn.Linear`. Transformers stacks all of a layer's experts into one three dimensional
`nn.Parameter`, which that walk never sees, so those weights stay exactly as they loaded.

Measured on Qwen3-Coder-30B-A3B-Instruct, **3.0%** of the layer weights are quantizable. Four
bit takes the model from 56.87 GB to 55.69 GB. On a dense model, where nearly all the weight is
in linear projections, the saving is the usual large one.

The planner knows this and sizes stages from what will actually be quantized, so a plan and a
load agree. `/health` reports `quantization_planned` and `quantization_applied` separately, and
a four bit load that cannot be honoured refuses rather than quietly loading at full precision.

Four bit needs `bitsandbytes`, which is in the CUDA lockfile only. On a machine without a
usable CUDA backend a four bit request is refused with the reason.

## Limits worth knowing before you start

- **Never run across two physical machines.** Everything verified so far has been one machine
  talking to itself over loopback. NAT, firewalls, real latency and cross-machine path agreement
  are all unexercised.
- **No failover.** Losing a machine loses the model until the pipeline is rebuilt.
- **Peers are addresses you type.** There is no discovery. That is deliberate: the mesh has
  discovery and building a second one here would be a mistake.
- **A model directory path must be valid on every machine.** The host names one path and every
  peer loads from it. If the model lives somewhere different on the second machine, it fails.
- **Caps.** A host runs 8 requests at once, accepts prompts up to 32768 tokens and generates at
  most 8192. A stage holds at most 16 connections and 8 requests.
