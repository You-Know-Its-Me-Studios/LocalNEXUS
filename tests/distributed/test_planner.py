"""The planner's invariants, which are what stop a plan producing a wrong model quietly.

Every one of these is a property that holds for any model and any set of machines, so they are
checked against a made up layer map rather than a real one. That is deliberate: a plan is
arithmetic over sizes, and using real weights here would make the tests slow, machine dependent,
and no more truthful.
"""
from __future__ import annotations

import pytest

from distributed import planner
from distributed.config import PlanError, NodeInfo, StagePlan
from distributed.layer_map import LayerMap, TensorRef

GB = 1024 ** 3


def _tensor(name: str, nbytes: int, shard: str = "s.safetensors") -> TensorRef:
    return TensorRef(name=name, shard=shard, dtype="BF16", shape=(nbytes // 2,),
                     begin=0, end=nbytes)


def _map(layers: int = 48, per_layer: int = 1 * GB, embed: int = GB // 2) -> LayerMap:
    """A model of a given shape, without any weights behind it."""
    built = LayerMap(model_dir=__import__("pathlib").Path("fake-model"), layer_count=layers)

    for index in range(layers):
        built.layers[index] = [
            _tensor(f"model.layers.{index}.self_attn.q_proj.weight", per_layer)
        ]

    built.embed = _tensor("model.embed_tokens.weight", embed)
    built.norm = _tensor("model.norm.weight", 4096)
    built.head = _tensor("lm_head.weight", embed)

    return built


def _node(name: str, gb: float) -> NodeInfo:
    return NodeInfo(node_id=name, host="10.0.0.1", port=8749,
                    vram_bytes=int(gb * GB), label=name)


def _covered(plan) -> list[int]:
    covered: list[int] = []

    for stage in plan.stages:
        covered.extend(range(stage.start_layer, stage.end_layer + 1))

    return covered


class TestCoverage:
    """Every layer held exactly once, by exactly one machine."""

    @pytest.mark.parametrize("sizes", [(80,), (40, 24), (24, 24, 24), (80, 4, 24), (16, 16, 16, 16)])
    def test_layers_are_contiguous_gapless_and_unique(self, sizes):
        model = _map()
        nodes = [_node(f"n{i}", gb) for i, gb in enumerate(sizes)]

        plan = planner.plan(model, nodes, margin_bytes=GB)

        assert _covered(plan) == list(range(model.layer_count))

    def test_the_embedding_is_on_the_first_stage_and_nowhere_else(self):
        plan = planner.plan(_map(), [_node("a", 24), _node("b", 24), _node("c", 24)],
                            margin_bytes=GB)

        assert [s.stage_index for s in plan.stages if s.includes_embed] == [0]

    def test_the_head_is_on_the_last_stage_and_nowhere_else(self):
        plan = planner.plan(_map(), [_node("a", 24), _node("b", 24), _node("c", 24)],
                            margin_bytes=GB)

        last = plan.stage_count - 1

        assert [s.stage_index for s in plan.stages if s.includes_head] == [last]

    def test_no_stage_is_given_more_than_its_machine_holds(self):
        sizes = [30, 20, 24]
        nodes = [_node(f"n{i}", gb) for i, gb in enumerate(sizes)]

        plan = planner.plan(_map(), nodes, margin_bytes=2 * GB)
        room = {n.node_id: n.vram_bytes - 2 * GB for n in nodes}

        for stage in plan.stages:
            assert stage.weight_bytes <= room[stage.node_id]

    def test_one_machine_that_fits_is_one_stage(self):
        plan = planner.plan(_map(), [_node("big", 200)], margin_bytes=GB)

        assert plan.stage_count == 1
        assert not plan.is_distributed
        assert plan.stages[0].includes_embed and plan.stages[0].includes_head


class TestRefusals:
    """A plan that cannot be made is refused, and the refusal is useful."""

    def test_machines_that_cannot_hold_it_are_refused_with_the_shortfall(self):
        with pytest.raises(PlanError) as caught:
            planner.plan(_map(), [_node("small", 8), _node("also-small", 8)], margin_bytes=GB)

        message = str(caught.value)

        assert "unplaced" in message
        assert "GB" in message

    def test_no_machines_at_all_is_refused(self):
        with pytest.raises(PlanError):
            planner.plan(_map(), [])

    def test_a_machine_too_small_for_one_layer_holds_nothing_and_is_named(self):
        plan = planner.plan(_map(), [_node("tiny", 1.2), _node("big", 200)], margin_bytes=GB)

        assert all(stage.node_id != "tiny" for stage in plan.stages)
        assert any("tiny" in note for note in plan.unused)


class TestRoundTrip:
    """A plan crosses a socket, so it has to survive being written down and read back."""

    def test_a_plan_survives_json(self):
        plan = planner.plan(_map(), [_node("a", 24), _node("b", 24), _node("c", 24)],
                            margin_bytes=GB)

        again = StagePlan.from_dict(plan.to_dict())

        assert again.to_dict() == plan.to_dict()

    def test_a_plan_with_a_gap_is_refused_on_the_way_back_in(self):
        plan = planner.plan(_map(), [_node("a", 24), _node("b", 24), _node("c", 24)],
                            margin_bytes=GB)

        document = plan.to_dict()
        document["stages"][1]["start_layer"] += 1        # a hole between stage 0 and stage 1

        with pytest.raises(PlanError):
            StagePlan.from_dict(document)

    def test_a_plan_missing_the_head_is_refused_on_the_way_back_in(self):
        plan = planner.plan(_map(), [_node("a", 24), _node("b", 24), _node("c", 24)],
                            margin_bytes=GB)

        document = plan.to_dict()
        document["stages"][-1]["includes_head"] = False

        with pytest.raises(PlanError):
            StagePlan.from_dict(document)


class TestQuantization:
    """Quantized weights are planned at the size they will actually occupy."""

    def test_a_quantized_plan_is_never_larger_than_an_unquantized_one(self):
        model = _map()
        nodes = [_node("a", 24), _node("b", 24), _node("c", 24)]

        plain = planner.plan(model, nodes, margin_bytes=GB, quantization="none")
        small = planner.plan(model, nodes, margin_bytes=GB, quantization="4bit")

        assert small.stages[0].weight_bytes <= plain.stages[0].weight_bytes
        assert small.quantization == "4bit"

    def test_an_unknown_quantization_is_refused(self):
        with pytest.raises(PlanError):
            planner.plan(_map(), [_node("a", 200)], quantization="3bit")
