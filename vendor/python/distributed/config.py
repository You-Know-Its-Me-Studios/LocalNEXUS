"""The things a pipeline is described by, and the only shapes that cross a machine boundary.

Everything here is data. A node reports what it has, the planner turns that into an assignment
per stage, and each stage is handed its own assignment and nothing else. Every one of these
converts to and from plain JSON, because that is what goes over the socket and what gets written
to a log a person can read afterwards.

There is no "the remote machine" here. A stage is a slot with a spec, and a node is something
that can fill one. Which of them happens to be this process is a single flag on the plan.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any

#: What is held back on every node for the things weights are not: the KV cache, the activation
#: being worked on, the allocator's own fragmentation, and whatever else is already on the card.
#: Two gigabytes is the spec's default and is deliberately generous, because overshooting is an
#: out of memory error part way through a load and undershooting only costs a layer.
DEFAULT_MARGIN_BYTES = 2 * 1024 ** 3

#: The port a stage listens on for activations when nobody says otherwise.
DEFAULT_STAGE_PORT = 8749

#: The port the host answers OpenAI requests on when nobody says otherwise.
DEFAULT_API_PORT = 8750


class PlanError(Exception):
    """A plan could not be made, or a plan that was made does not hold together."""


@dataclass(frozen=True)
class NodeInfo:
    """A machine offering to hold part of a model, as it describes itself.

    ``vram_bytes`` is what the node says it has free for weights, not what the card has in total.
    A node with no usable accelerator reports what it will give up of system memory and sets
    ``device`` to cpu, which works and is slow, and saying so is better than refusing.
    """

    node_id: str
    host: str
    port: int = DEFAULT_STAGE_PORT
    vram_bytes: int = 0
    device: str = "cpu"

    #: What to call this in a log or on screen. Falls back to the id, which is always present.
    label: str = ""

    @property
    def address(self) -> str:
        """host:port, which is how a stage is reached and how it reads in a log."""
        return f"{self.host}:{self.port}"

    @property
    def display_name(self) -> str:
        return self.label or self.node_id

    def to_dict(self) -> dict[str, Any]:
        return {
            "node_id": self.node_id,
            "host": self.host,
            "port": self.port,
            "vram_bytes": self.vram_bytes,
            "device": self.device,
            "label": self.label,
        }

    @staticmethod
    def from_dict(document: dict[str, Any]) -> NodeInfo:
        try:
            return NodeInfo(
                node_id=str(document["node_id"]),
                host=str(document["host"]),
                port=int(document.get("port", DEFAULT_STAGE_PORT)),
                vram_bytes=int(document.get("vram_bytes", 0)),
                device=str(document.get("device", "cpu")),
                label=str(document.get("label", "")),
            )
        except (KeyError, TypeError, ValueError) as error:
            raise PlanError(f"A node description could not be read: {error}") from error


@dataclass(frozen=True)
class StageAssignment:
    """One machine's share of a model: which layers, and what else rides with them.

    Layer bounds are inclusive on both ends, because a stage holding only layer 12 is
    ``start == end == 12`` and a half open range would have to say 12 to 13 for it.
    """

    stage_index: int
    node_id: str
    host: str
    port: int
    start_layer: int
    end_layer: int
    includes_embed: bool
    includes_head: bool

    #: What this stage is expected to hold in weights, from the layer map. It is what the plan
    #: was decided on, so it is carried with the plan rather than recomputed by the reader.
    weight_bytes: int = 0

    @property
    def layer_count(self) -> int:
        return self.end_layer - self.start_layer + 1

    @property
    def address(self) -> str:
        return f"{self.host}:{self.port}"

    @property
    def is_first(self) -> bool:
        """The stage that turns tokens into hidden states, which is where a request enters."""
        return self.includes_embed

    @property
    def is_last(self) -> bool:
        """The stage that produces logits, which is where a token comes from."""
        return self.includes_head

    def describe(self) -> str:
        extras = []

        if self.includes_embed:
            extras.append("embedding")
        if self.includes_head:
            extras.append("norm and head")

        tail = f", plus the {' and the '.join(extras)}" if extras else ""

        return (
            f"stage {self.stage_index} on {self.node_id} at {self.address}: "
            f"layers {self.start_layer} to {self.end_layer} "
            f"({self.layer_count} of them){tail}"
        )

    def to_dict(self) -> dict[str, Any]:
        return {
            "stage_index": self.stage_index,
            "node_id": self.node_id,
            "host": self.host,
            "port": self.port,
            "start_layer": self.start_layer,
            "end_layer": self.end_layer,
            "includes_embed": self.includes_embed,
            "includes_head": self.includes_head,
            "weight_bytes": self.weight_bytes,
        }

    @staticmethod
    def from_dict(document: dict[str, Any]) -> StageAssignment:
        try:
            return StageAssignment(
                stage_index=int(document["stage_index"]),
                node_id=str(document["node_id"]),
                host=str(document["host"]),
                port=int(document["port"]),
                start_layer=int(document["start_layer"]),
                end_layer=int(document["end_layer"]),
                includes_embed=bool(document["includes_embed"]),
                includes_head=bool(document["includes_head"]),
                weight_bytes=int(document.get("weight_bytes", 0)),
            )
        except (KeyError, TypeError, ValueError) as error:
            raise PlanError(f"A stage assignment could not be read: {error}") from error


@dataclass
class StagePlan:
    """The whole pipeline: who holds what, in the order activations travel.

    ``stages`` is in pipeline order and its index is the stage index, so stage n forwards to
    stage n + 1 and the last one forwards to nobody.
    """

    model_dir: str
    layer_count: int
    stages: list[StageAssignment] = field(default_factory=list)
    margin_bytes: int = DEFAULT_MARGIN_BYTES

    #: Nodes that were offered and hold nothing, with why. A node too small for a single layer
    #: is not an error and is not silently dropped either, because somebody expecting their
    #: machine to be in the pipeline deserves to be told it is not.
    unused: list[str] = field(default_factory=list)

    @property
    def stage_count(self) -> int:
        return len(self.stages)

    @property
    def is_distributed(self) -> bool:
        """More than one machine, which is the only case this whole package is for."""
        return len(self.stages) > 1

    def for_node(self, node_id: str) -> StageAssignment:
        """This node's share, which is all a stage process ever needs to know."""
        for stage in self.stages:
            if stage.node_id == node_id:
                return stage

        raise PlanError(f"{node_id} holds no stage in this plan.")

    def next_of(self, stage_index: int) -> StageAssignment | None:
        """Where a stage forwards to, or nothing if it is the end of the pipeline."""
        following = stage_index + 1

        return self.stages[following] if 0 <= following < len(self.stages) else None

    def validate(self) -> None:
        """Refuses a plan that would load and then quietly produce nonsense.

        Every check here is something that survives loading. A gap in the layers, a layer held
        twice, or a missing head all come back as fluent output that is wrong, which is the most
        expensive kind of failure to notice, so the plan is checked rather than trusted.

        Raises:
            PlanError: the plan does not cover the model exactly once.
        """
        if not self.stages:
            raise PlanError("The plan has no stages, so nothing holds the model.")

        expected_start = 0

        for position, stage in enumerate(self.stages):
            if stage.stage_index != position:
                raise PlanError(
                    f"Stage at position {position} calls itself stage {stage.stage_index}."
                )

            if stage.end_layer < stage.start_layer:
                raise PlanError(
                    f"Stage {position} holds layers {stage.start_layer} to {stage.end_layer}, "
                    "which is backwards."
                )

            if stage.start_layer != expected_start:
                raise PlanError(
                    f"Stage {position} starts at layer {stage.start_layer} and the stage before "
                    f"it ended at {expected_start - 1}. The layers are not contiguous."
                )

            expected_start = stage.end_layer + 1

        if expected_start != self.layer_count:
            raise PlanError(
                f"The stages cover layers 0 to {expected_start - 1} and the model has "
                f"{self.layer_count}. {self.layer_count - expected_start} layer(s) are unheld."
            )

        embed_holders = [s.stage_index for s in self.stages if s.includes_embed]
        head_holders = [s.stage_index for s in self.stages if s.includes_head]

        if embed_holders != [0]:
            raise PlanError(
                f"The embedding has to be on stage 0 and nowhere else. It is on {embed_holders}."
            )

        last = len(self.stages) - 1

        if head_holders != [last]:
            raise PlanError(
                f"The norm and head have to be on stage {last} and nowhere else. "
                f"They are on {head_holders}."
            )

        node_ids = [stage.node_id for stage in self.stages]

        if len(set(node_ids)) != len(node_ids):
            raise PlanError(f"A node holds more than one stage: {node_ids}.")

    def describe(self) -> str:
        """The plan as something a person can check, which is the point of showing it at all."""
        lines = [
            f"{self.stage_count} stage(s) over {self.layer_count} layers, "
            f"{self.margin_bytes / 1024 ** 3:.1f} GB held back per node"
        ]

        lines.extend(f"  {stage.describe()}, {stage.weight_bytes / 1024 ** 3:.2f} GB"
                     for stage in self.stages)
        lines.extend(f"  unused: {note}" for note in self.unused)

        return "\n".join(lines)

    def to_dict(self) -> dict[str, Any]:
        return {
            "model_dir": self.model_dir,
            "layer_count": self.layer_count,
            "margin_bytes": self.margin_bytes,
            "stages": [stage.to_dict() for stage in self.stages],
            "unused": list(self.unused),
        }

    @staticmethod
    def from_dict(document: dict[str, Any]) -> StagePlan:
        try:
            plan = StagePlan(
                model_dir=str(document["model_dir"]),
                layer_count=int(document["layer_count"]),
                stages=[StageAssignment.from_dict(entry) for entry in document["stages"]],
                margin_bytes=int(document.get("margin_bytes", DEFAULT_MARGIN_BYTES)),
                unused=[str(note) for note in document.get("unused", [])],
            )
        except (KeyError, TypeError, ValueError) as error:
            raise PlanError(f"A stage plan could not be read: {error}") from error

        plan.validate()

        return plan
