"""Reading a real safetensors model, and the split-equivalence property.

These need real weights. A checkout has none and never will, so they skip rather than fail when
there is no model on the machine, the same way the C# end to end layer does.

The last test in this file is the one the whole design rests on: layers run as two stages have
to give bit for bit the same hidden states as the same layers run as one. If that is ever false,
every distributed answer is subtly wrong and nothing else here would notice.
"""
from __future__ import annotations

import pytest

from distributed import layer_map
from distributed.layer_map import LayerMapError


@pytest.fixture(scope="module")
def built(real_model_dir):
    if real_model_dir is None:
        pytest.skip("no safetensors model on this machine, so there is nothing to read")

    return layer_map.build(real_model_dir)


class TestReadingAModel:
    def test_the_layers_are_contiguous_from_zero(self, built):
        assert sorted(built.layers) == list(range(built.layer_count))

    def test_every_layer_holds_something(self, built):
        assert all(built.layer_bytes(index) > 0 for index in built.layers)

    def test_the_embedding_and_the_head_are_found(self, built):
        assert built.embed is not None, "no embedding, so nothing can turn tokens into states"
        assert built.head is not None or built.tied_embeddings, \
            "no head and no tying, so nothing can turn states into tokens"

    def test_nothing_is_left_unplaced(self, built):
        assert built.unplaced == [], f"unrecognised tensors: {[r.name for r in built.unplaced]}"

    def test_the_total_is_the_sum_of_the_parts(self, built):
        parts = built.total_layer_bytes + built.embed_bytes + built.tail_bytes

        assert built.total_bytes == parts

    def test_a_folder_that_is_not_a_model_is_refused(self, tmp_path):
        with pytest.raises(LayerMapError):
            layer_map.build(tmp_path)

    def test_a_path_that_does_not_exist_is_refused(self, tmp_path):
        with pytest.raises(LayerMapError):
            layer_map.build(tmp_path / "nothing-here")


class TestQuantizableSizing:
    """What will actually shrink, which is not the same as what was asked to shrink."""

    def test_expert_tensors_are_not_counted_as_quantizable(self, built):
        experts = [ref for tensors in built.layers.values() for ref in tensors
                   if ".experts." in ref.name]

        if not experts:
            pytest.skip("this model has no mixture of experts layers")

        assert all(not layer_map.is_quantizable(ref) for ref in experts)

    def test_a_quantized_layer_is_never_larger_than_the_stored_one(self, built):
        for index in list(built.layers)[:4]:
            assert built.held_layer_bytes(index, 0.3) <= built.layer_bytes(index)

    def test_the_embedding_never_quantizes(self, built):
        assert built.held_embed_bytes(0.3) == built.embed_bytes


class TestSplitEquivalence:
    """The property the whole design rests on."""

    def test_two_stages_give_the_same_hidden_states_as_one(self, real_model_dir):
        if real_model_dir is None:
            pytest.skip("no safetensors model on this machine")

        torch = pytest.importorskip("torch")

        if not torch.cuda.is_available():
            pytest.skip("no CUDA device, and this is far too slow to be worth doing on the processor")

        from distributed.config import StageAssignment
        from distributed.partial_loader import PartialStage

        prompt = torch.tensor([[3838, 374, 279, 6722, 315, 9625, 30]], dtype=torch.int64)

        def assignment(index, start, end, embed):
            return StageAssignment(stage_index=index, node_id=f"n{index}", host="127.0.0.1",
                                   port=9000 + index, start_layer=start, end_layer=end,
                                   includes_embed=embed, includes_head=False)

        def run(stages, ids):
            carried = ids
            for stage in stages:
                carried = stage.step("r", carried, 0, {"temperature": 0.0})
            return carried

        whole = PartialStage(str(real_model_dir), assignment(0, 0, 3, True))
        whole.load()
        reference = run([whole], prompt).cpu().clone()
        del whole
        torch.cuda.empty_cache()

        first = PartialStage(str(real_model_dir), assignment(0, 0, 1, True))
        first.load()
        second = PartialStage(str(real_model_dir), assignment(1, 2, 3, False))
        second.load()
        split = run([first, second], prompt).cpu()

        try:
            assert split.shape == reference.shape
            assert torch.equal(split, reference), (
                "splitting the same layers across two stages changed the hidden states. "
                "Every distributed answer would be subtly wrong."
            )
        finally:
            del first, second
            torch.cuda.empty_cache()
