"""Pipeline parallel inference for safetensors models across machines.

llama.cpp serves GGUF locally and Mesh LLM distributes GGUF across machines. A safetensors model
has neither: transformers serves it on one machine, and a model that does not fit on that machine
has nowhere to go. This package is that missing path, and only that path.

A model is cut into contiguous ranges of decoder layers, one range per machine. Each machine loads
only its own range, and a request walks the machines in order carrying hidden states rather than
tokens. The first machine embeds, the last one applies the final norm and the head and samples.

Distribution is a capability unlock, not a speedup. Every stage boundary is a network hop, so a
model that fits on one machine is served by one machine.
"""

from __future__ import annotations

__all__ = [
    "activation_server",
    "api",
    "config",
    "coordinator",
    "layer_map",
    "partial_loader",
    "planner",
    "protocol",
]
