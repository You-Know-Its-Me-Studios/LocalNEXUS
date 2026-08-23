"""Deciding which machine holds which layers.

The rule is greedy and deliberately dull: walk the nodes in the order they were offered, fill
each one from layer 0 upward, and cut where the next layer would not fit in what is left of its
memory after the safety margin. A cleverer allocator would balance the stages, and balancing is
the wrong objective: a pipeline runs one stage at a time, so an even split makes every stage
equally slow rather than making the whole thing faster. What matters is that the model fits at
all, and that it fits in as few stages as possible, because every stage boundary is a network
hop.

Two things ride along with the layers and are not free. The first stage holds the token
embedding, and the last one holds the final norm and the head. On the 30B model each of those is
about half a layer, so they are charged to the budget rather than waved through.
"""

from __future__ import annotations

from collections.abc import Iterable, Sequence

from .config import DEFAULT_MARGIN_BYTES, NodeInfo, PlanError, StageAssignment, StagePlan
from .layer_map import LayerMap


def plan(
    layer_map: LayerMap,
    nodes: Sequence[NodeInfo],
    margin_bytes: int = DEFAULT_MARGIN_BYTES,
) -> StagePlan:
    """Works out which node holds which layers, in the order the nodes were offered.

    Order is the caller's, not this function's, and it is meaningful twice over. The first node
    that can hold anything becomes stage 0 and takes the embedding, and stage order is the order
    activations travel, so it is also the network path. The coordinator puts itself first because
    the host answers the API and so is where a request already is.

    A plan with one stage is a valid answer and means the model fits on one machine. That is not
    a pipeline and should not be run as one, which the caller decides by asking the plan.

    Raises:
        PlanError: no arrangement of these nodes holds the whole model.
    """
    if not nodes:
        raise PlanError("No nodes were offered, so there is nothing to plan across.")

    if margin_bytes < 0:
        raise PlanError(f"The safety margin cannot be negative, and it is {margin_bytes}.")

    last_layer = layer_map.layer_count - 1
    next_layer = 0

    stages: list[StageAssignment] = []
    unused: list[str] = []

    for node in nodes:
        if next_layer > last_layer:
            unused.append(
                f"{node.display_name} holds nothing, because the model was covered before it "
                "was reached. Fewer stages is faster, so this is the good outcome."
            )
            continue

        # The embedding is charged to whichever node becomes stage 0, which is the first one able
        # to hold a layer at all rather than simply the first one offered.
        takes_embed = not stages
        budget = node.vram_bytes - margin_bytes - (layer_map.embed_bytes if takes_embed else 0)

        start = next_layer
        held = 0

        while next_layer <= last_layer:
            # Whoever takes the final layer takes the norm and the head with it, so the fit is
            # tested against both together. A node that can hold the last layer but not the head
            # cannot be the last stage, and this is where that is found rather than at load time.
            needed = layer_map.layer_bytes(next_layer)

            if next_layer == last_layer:
                needed += layer_map.last_stage_extra

            if needed > budget:
                break

            budget -= needed
            held += needed
            next_layer += 1

        if next_layer == start:
            unused.append(_why_nothing_fits(node, layer_map, margin_bytes, start, takes_embed))
            continue

        stages.append(
            StageAssignment(
                stage_index=len(stages),
                node_id=node.node_id,
                host=node.host,
                port=node.port,
                start_layer=start,
                end_layer=next_layer - 1,
                includes_embed=takes_embed,
                includes_head=next_layer - 1 == last_layer,
                weight_bytes=held + (layer_map.embed_bytes if takes_embed else 0),
            )
        )

    if next_layer <= last_layer:
        raise PlanError(_why_it_does_not_fit(layer_map, nodes, margin_bytes, next_layer))

    built = StagePlan(
        model_dir=str(layer_map.model_dir),
        layer_count=layer_map.layer_count,
        stages=stages,
        margin_bytes=margin_bytes,
        unused=unused,
    )

    # The plan is checked against the same rules a plan arriving over the wire is checked
    # against. A planner that trusts its own output is a planner whose bugs reach the loader.
    built.validate()

    return built


def _why_nothing_fits(
    node: NodeInfo,
    layer_map: LayerMap,
    margin_bytes: int,
    layer: int,
    takes_embed: bool,
) -> str:
    """Why one node ended up holding no layers, in the terms the person offering it would use."""
    budget = node.vram_bytes - margin_bytes - (layer_map.embed_bytes if takes_embed else 0)
    needed = layer_map.layer_bytes(layer)

    if layer == layer_map.layer_count - 1:
        needed += layer_map.last_stage_extra

    if budget <= 0:
        return (
            f"{node.display_name} holds nothing: it offers {_gb(node.vram_bytes)} and "
            f"{_gb(margin_bytes)} is held back, which leaves nothing for weights."
        )

    return (
        f"{node.display_name} holds nothing: layer {layer} needs {_gb(needed)} and only "
        f"{_gb(budget)} is usable there."
    )


def _why_it_does_not_fit(
    layer_map: LayerMap,
    nodes: Iterable[NodeInfo],
    margin_bytes: int,
    first_unplaced: int,
) -> str:
    """The refusal, with the number somebody would need in order to fix it.

    Refusing without saying how short it is turns a solvable problem into a guessing game, and
    the shortfall is the one number that makes the difference between adding a machine and
    adding the right machine.
    """
    remaining = sum(
        layer_map.layer_bytes(index) for index in range(first_unplaced, layer_map.layer_count)
    ) + layer_map.last_stage_extra

    offered = sum(node.vram_bytes for node in nodes)
    usable = offered - margin_bytes * sum(1 for _ in nodes)

    return (
        f"These machines cannot hold {layer_map.model_dir.name}. "
        f"Layers {first_unplaced} to {layer_map.layer_count - 1} and the head are unplaced, "
        f"which is {_gb(remaining)} with nowhere to go. "
        f"The model needs {_gb(layer_map.total_bytes)}, the machines offer {_gb(offered)}, and "
        f"{_gb(usable)} of that is usable once {_gb(margin_bytes)} per machine is held back."
    )


def _gb(nbytes: int) -> str:
    return f"{nbytes / 1024 ** 3:.2f} GB"
