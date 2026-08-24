# The mesh: sharing GGUF models between machines

> **This is one of two ways to use more than one machine, and they are unrelated.**
>
> This page is the mesh: GGUF models, served whole, shared with other people, discovery
> included. If instead you have a **safetensors** model too large for one machine and you want
> to spread its layers over machines of your own, that is a different feature entirely and it is
> in [distributed-safetensors.md](distributed-safetensors.md). The mesh cannot serve safetensors
> and the safetensors pipeline cannot serve GGUF.


One model can run across several machines over [Mesh LLM](https://github.com/Mesh-LLM/mesh-llm),
which LocalNEXUS starts as a silent child process. The mesh pools the GPUs of every machine in
it behind one OpenAI-compatible API, routes each request to whichever peer can serve the model,
and splits models too large for one box into contiguous layer stages.

Local single machine inference does not use any of this. A model that fits on one GPU is served
by llama.cpp exactly as it always was, whether or not a mesh node is running.

**Place the engine.** Put a Mesh LLM Windows build in `vendor\mesh` (see
`vendor\mesh\README.md` for which flavour and the expected layout). A published `dist\`
folder already carries it.

**Start your node.** Open the **Network** tab and press **Start mesh node**. With nothing else
configured this hosts a private mesh on the local network: LAN-scoped discovery only, no public
relays, joinable only by the invite token shown on the card. Publishing it for public discovery
is a separate tick box and is the only setting that reaches beyond your network.

**Contribute.** Tick **Offer this machine's compute**, choose which local GGUF this machine
serves, and press **Apply**. Unlike the previous engine's declared offer, the memory cap here is
enforced by the mesh planner: a model that does not fit inside it is never placed on this
machine.

**Add a second machine.** Install LocalNEXUS there, copy the invite token from the first
machine's Network tab, press **+** next to Sources, paste it, and press **Join**. Both machines
then appear in each other's source lists with their announced memory and measured latency. The
first time, allow the node through the Windows firewall.

**Browse what the network can serve.** The Available models list leads the tab: every model the
mesh knows about, its metadata, how many sources hold pieces of it, and a verdict. Selecting one
draws its coverage chain: the pipeline of sections in order, which source holds which layer
range, and how much slack stands behind each.

The verdict is three way, because a model that is still loading has not failed at anything. A
section still coming up is blue and says what it is waiting on; only a section the mesh is known
to be unable to serve, because no source holds it or the source holding it reports it stopped,
is red and named as the reason the model is Blocked. The same distinction runs through the tab:
before the node has answered, the model and source lists say the mesh is starting rather than
sitting empty as though the network were bare.

**Run across the mesh.** Set a Model node's provider to **Network** and pick a model from the
list. Only a Complete model can be picked, and a model that stops being complete between
selection and run refuses the run, saying whether it is still coming up or blocked outright. Whether the model runs on one peer or as layer stages
across several is the mesh's decision, made at run time and echoed to the activity feed.

Nothing about sources is configured by hand any more. Membership, placement, liveness and
recovery all belong to the engine; the Network tab renders what it reports.

Nothing LocalNEXUS starts outlives it. Engine processes are held by the operating system on the
application's behalf and are stopped when it closes, whether it closes normally or is killed
outright, and anything a previous session left behind is cleaned up at the next launch. An engine
you started yourself is never touched.

Four things worth knowing about the layer underneath, all established by testing the bundled
build rather than by reading its documentation:

- **A splittable model is not the same as a local GGUF.** Stage splits need a published layer
  package (a repository of per-layer GGUF fragments). A plain local GGUF can be served whole by
  one machine and routed to across the mesh, but it cannot be split.
- **The memory cap is real.** `--max-vram` is honoured by the planner, which will refuse to
  place a model that does not fit rather than trying and failing.
- **One node can serve many consumers at once.** This is the limitation that ended the previous
  engine, and it is genuinely gone.
- **A peer dying mid request no longer takes the pipeline down.** An in-flight streaming request
  survived its stage peer being killed. Re-convergence afterwards is less certain: on a single
  GPU loopback topology the mesh replanned onto a replacement node but did not become routable
  again within the test window. Treat recovery-after-replacement as unproven rather than
  guaranteed.

