"""Holding part of a model, and running that part.

A stage builds the whole model object and then loads weights into only the modules it was
assigned. Everything else stays on the meta device, which is torch's word for a tensor that has
a shape and a dtype and no memory at all. The model is therefore complete as far as Python is
concerned and costs only its own share in bytes.

That is done by handing ``from_pretrained`` a device map naming every module and sending the ones
this stage does not hold to ``meta``. It matters that the loader is the one doing this rather
than a hand written pass over the shards, because a checkpoint's layout is not the model's
layout. This model stores its mixture of experts as three separate tensors for each of a hundred
and twenty eight experts per layer, and the module wants two stacked tensors, so eighteen
thousand tensors on disk become five hundred and thirty one in memory. Reproducing that fusion
by hand would mean reproducing it again for the next architecture, and being wrong about it
produces a model that loads cleanly and generates nonsense.

The forward pass is written out here rather than borrowed, because the model's own forward runs
every layer and then the final norm, and a stage has neither all the layers nor, usually, the
norm. What it does is the same work in the same order, with the same mask, the same rotary
embeddings and the same cache, over the layers this stage actually holds.
"""

from __future__ import annotations

import logging
from typing import Any

import torch
from transformers import AutoConfig, AutoModelForCausalLM, DynamicCache
from transformers.masking_utils import create_causal_mask, create_sliding_window_causal_mask

from .config import QUANTIZATION_4BIT, QUANTIZATION_NONE, StageAssignment

_log = logging.getLogger(__name__)

#: Where a module goes when this stage does not hold it. A meta tensor has a shape and no
#: storage, so the module exists, reports its size, and occupies nothing.
NOWHERE = "meta"


class LoadError(Exception):
    """The assigned slice could not be loaded, with the reason."""


class PartialStage:
    """One stage's share of a model: some layers, and possibly the ends."""

    def __init__(
        self,
        model_dir: str,
        assignment: StageAssignment,
        device: str | None = None,
        dtype: torch.dtype | str = "auto",
        quantization: str = QUANTIZATION_NONE,
    ) -> None:
        self._model_dir = model_dir
        self._assignment = assignment
        self._device = device or _best_device()
        self._dtype_request = dtype
        self._quantization = (quantization or QUANTIZATION_NONE).lower()

        # What was actually done, which is not always what was asked for. Quantizing needs a
        # package that may not be installed, and a stage that quietly loads at full precision
        # after being told to quantize is a stage that runs out of memory for reasons nobody can
        # see. This is reported rather than inferred.
        self._quantization_applied = QUANTIZATION_NONE

        self._config: Any = None
        self._model: Any = None
        self._inner: Any = None
        self._layers: list[torch.nn.Module] = []

        # A cache per request, holding only this stage's layers. Nothing about it is shared with
        # any other machine, and it must not be: the whole reason a stage is cheap to talk to is
        # that its cache never leaves it.
        self._caches: dict[str, DynamicCache] = {}

        # What the last stage has produced so far per request, which is what a repetition penalty
        # is applied against. Only the last stage fills this.
        self._history: dict[str, list[int]] = {}

    @property
    def assignment(self) -> StageAssignment:
        return self._assignment

    @property
    def device(self) -> str:
        return self._device

    @property
    def dtype(self) -> torch.dtype:
        return self._model.dtype if self._model is not None else torch.bfloat16

    @property
    def hidden_size(self) -> int:
        return int(self._config.hidden_size)

    @property
    def quantization(self) -> str:
        """What this stage actually loaded with, which may not be what it was asked for."""
        return self._quantization_applied

    @property
    def config(self) -> Any:
        """The model's own config, which the API reads for its stop tokens and context length."""
        if self._config is None:
            raise LoadError("This stage has not loaded anything yet.")

        return self._config

    def load(self) -> None:
        """Reads this stage's weights and nothing else.

        Raises:
            LoadError: the model could not be read, or the assignment does not fit the model.
        """
        try:
            self._config = AutoConfig.from_pretrained(self._model_dir)
        except Exception as error:  # noqa: BLE001 - anything here is the same answer to a caller
            raise LoadError(f"{self._model_dir} has no config that could be read: {error}") from error

        layers = int(self._config.num_hidden_layers)

        if self._assignment.end_layer >= layers:
            raise LoadError(
                f"This stage was assigned layers {self._assignment.start_layer} to "
                f"{self._assignment.end_layer} and the model has {layers}."
            )

        placement = self._device_map(layers)
        extra = self._quantization_arguments()

        try:
            self._model = AutoModelForCausalLM.from_pretrained(
                self._model_dir,
                dtype=self._dtype_request,
                device_map=placement,
                **extra,
            )
        except Exception as error:  # noqa: BLE001 - the reason has to reach the coordinator
            raise LoadError(f"This stage's layers could not be loaded: {error}") from error

        self._model.eval()
        self._inner = self._model.model

        # The layers this stage runs, in order, held directly. Indexing the model's own list by
        # position would be wrong here and quietly so, because the list still has an entry for
        # every layer in the model and all but this stage's are empty.
        self._layers = [self._inner.layers[index]
                        for index in range(self._assignment.start_layer,
                                           self._assignment.end_layer + 1)]

        _log.info("stage %d loaded %s on %s",
                  self._assignment.stage_index, self.describe(), self._device)

    def _quantization_arguments(self) -> dict[str, Any]:
        """The quantization to load with, or a refusal saying why it cannot be done.

        This used to fall back to full precision with a warning, and that was wrong. The plan
        that placed this stage was worked out on the quantized size, so a stage that quietly
        loads at three times that figure is not a degraded pipeline, it is a pipeline whose
        arithmetic no longer describes it: every stage after this one was sized on the same
        assumption, and the machine that was going to hold forty layers now cannot hold twelve.
        Failing here costs a clear message. Continuing costs an out of memory error somewhere
        else, attributed to the wrong machine.

        Raises:
            LoadError: four bit weights were asked for and cannot be produced.
        """
        if self._quantization == QUANTIZATION_NONE:
            self._quantization_applied = QUANTIZATION_NONE
            return {}

        if self._quantization != QUANTIZATION_4BIT:
            raise LoadError(
                f"{self._quantization!r} is not a quantization this build knows. "
                f"It is one of: {QUANTIZATION_NONE}, {QUANTIZATION_4BIT}."
            )

        unusable = _why_no_bitsandbytes()

        if unusable is not None:
            raise LoadError(
                "Four bit weights were asked for and cannot be produced on this machine, so "
                "the pipeline is refused rather than loaded at a size nothing planned for. "
                f"{unusable}"
            )

        from transformers import BitsAndBytesConfig

        self._quantization_applied = QUANTIZATION_4BIT

        return {
            "quantization_config": BitsAndBytesConfig(
                load_in_4bit=True,
                # NF4 with double quantization, which is the arrangement the plan's size estimate
                # assumes. Computing in bfloat16 keeps the arithmetic at the dtype the rest of the
                # pipeline moves activations in, so a hidden state does not change type at a
                # stage boundary purely because that stage happened to be quantized.
                bnb_4bit_quant_type="nf4",
                bnb_4bit_use_double_quant=True,
                bnb_4bit_compute_dtype=torch.bfloat16,
            )
        }

    def _device_map(self, layer_count: int) -> dict[str, Any]:
        """Names every module and says where it goes, which is how only a slice gets read.

        Every module in the model has to appear. A module the map does not mention is placed by
        the loader's own judgement, which for a model larger than the card means offloading it to
        disk, and a stage that quietly starts paging weights off an SSD looks like a slow network
        rather than like the mistake it is.
        """
        held = self._assignment
        placement: dict[str, Any] = {}

        placement["model.embed_tokens"] = self._device if held.includes_embed else NOWHERE
        placement["model.rotary_emb"] = self._device
        placement["model.norm"] = self._device if held.includes_head else NOWHERE
        placement["lm_head"] = self._device if held.includes_head else NOWHERE

        for index in range(layer_count):
            inside = held.start_layer <= index <= held.end_layer
            placement[f"model.layers.{index}"] = self._device if inside else NOWHERE

        # When a model ties its head to its embedding there is no separate head tensor, so the
        # last stage needs the embedding in order to have a head at all. It is loaded twice
        # across the pipeline in that case, which is a real cost and is the planner's to know
        # about rather than a surprise here.
        if getattr(self._config, "tie_word_embeddings", False) and held.includes_head:
            placement["model.embed_tokens"] = self._device

        return placement

    def step(
        self,
        request_id: str,
        incoming: torch.Tensor,
        position_offset: int,
        sampling: dict[str, Any],
    ) -> torch.Tensor:
        """Runs this stage's layers for one request at one step.

        The first stage is given token ids and returns hidden states. A middle stage is given
        hidden states and returns hidden states. The last stage is given hidden states and
        returns the one token it sampled.
        """
        if self._model is None:
            raise LoadError("This stage has not loaded anything yet.")

        with torch.inference_mode():
            cache = self._caches.get(request_id)

            if cache is None:
                cache = DynamicCache(config=self._config)
                self._caches[request_id] = cache

            hidden = self._entering(incoming)
            length = hidden.shape[1]

            # Position is carried in the frame rather than counted here, because it is the one
            # thing a stage cannot work out for itself: its cache knows how many tokens it has
            # seen, and on the first step of a request that is zero on every stage at once.
            position_ids = (
                torch.arange(length, device=hidden.device, dtype=torch.long) + position_offset
            ).unsqueeze(0)

            build_mask = (
                create_causal_mask
                if getattr(self._config, "sliding_window", None) is None
                else create_sliding_window_causal_mask
            )

            mask = build_mask(
                config=self._config,
                inputs_embeds=hidden,
                attention_mask=None,
                past_key_values=cache,
                position_ids=position_ids,
                # The mask is sized from a layer this stage actually holds. Left to itself the
                # loader sizes it from layer 0, which on any stage but the first is an empty
                # cache slot, so every decode step would be masked as though it were the start
                # of the sequence.
                layer_idx=self._assignment.start_layer,
            )

            position_embeddings = self._inner.rotary_emb(hidden, position_ids=position_ids)

            for layer in self._layers:
                hidden = layer(
                    hidden,
                    attention_mask=mask,
                    position_ids=position_ids,
                    past_key_values=cache,
                    use_cache=True,
                    position_embeddings=position_embeddings,
                )

            if not self._assignment.includes_head:
                return hidden

            hidden = self._inner.norm(hidden)

            # Only the last position can produce the next token, and the head is the widest
            # matrix in the model, so running it over the whole prompt would cost as much as
            # several layers to produce logits that are then thrown away.
            logits = self._model.lm_head(hidden[:, -1:, :])

            return self._sample(request_id, logits[0, -1].float(), sampling)

    def _entering(self, incoming: torch.Tensor) -> torch.Tensor:
        """Whatever arrived, as hidden states on this stage's device."""
        if self._assignment.includes_embed:
            return self._inner.embed_tokens(incoming.to(self._device, dtype=torch.long))

        return incoming.to(device=self._device, dtype=self.dtype)

    def _sample(
        self,
        request_id: str,
        logits: torch.Tensor,
        sampling: dict[str, Any],
    ) -> torch.Tensor:
        """Picks the next token, and remembers it so a repetition penalty has something to read.

        The history starts as whatever the host sent with the prompt, because a penalty applied
        only to what has been generated does not see the request it is answering, and a model
        will happily repeat the question back.
        """
        history = self._history.get(request_id)

        if history is None:
            history = [int(token) for token in sampling.get("history", [])]
            self._history[request_id] = history

        penalty = float(sampling.get("repetition_penalty", 1.0))

        if penalty != 1.0 and history:
            seen = torch.tensor(sorted(set(history)), device=logits.device, dtype=torch.long)
            scores = logits.index_select(0, seen)

            # The usual asymmetric form: a positive score is divided and a negative one is
            # multiplied, so both move away from being chosen.
            logits = logits.index_copy(
                0, seen, torch.where(scores > 0, scores / penalty, scores * penalty)
            )

        temperature = float(sampling.get("temperature", 1.0))
        top_k = int(sampling.get("top_k", 0) or 0)
        top_p = float(sampling.get("top_p", 1.0))

        if temperature <= 0:
            chosen = int(torch.argmax(logits).item())
        else:
            logits = logits / temperature

            if 0 < top_k < logits.numel():
                cutoff = torch.topk(logits, top_k).values[-1]
                logits = logits.masked_fill(logits < cutoff, float("-inf"))

            if 0.0 < top_p < 1.0:
                ordered, order = torch.sort(logits, descending=True)
                running = torch.cumsum(torch.softmax(ordered, dim=-1), dim=-1)

                # Keep everything up to and including the one that crosses the threshold, so the
                # most likely token survives even when it is already above top_p on its own.
                drop = running - torch.softmax(ordered, dim=-1) >= top_p
                ordered = ordered.masked_fill(drop, float("-inf"))
                logits = torch.full_like(logits, float("-inf")).scatter(0, order, ordered)

            chosen = int(torch.multinomial(torch.softmax(logits, dim=-1), num_samples=1).item())

        history.append(chosen)

        return torch.tensor([[chosen]], dtype=torch.int64)

    def release(self, request_id: str) -> None:
        """Drops everything held for a request."""
        self._caches.pop(request_id, None)
        self._history.pop(request_id, None)

    def describe(self) -> str:
        held = self._assignment
        parts = [f"layers {held.start_layer} to {held.end_layer}"]

        if held.includes_embed:
            parts.append("the embedding")
        if held.includes_head:
            parts.append("the norm and head")

        if self._quantization_applied != QUANTIZATION_NONE:
            parts.append(f"quantized to {self._quantization_applied}")

        return ", ".join(parts)


def _why_no_bitsandbytes() -> str | None:
    """Nothing if four bit weights can really be produced here, or the reason they cannot.

    Three separate questions, because passing the first two and failing the third is exactly the
    state that wasted a round of this work. ``BitsAndBytesConfig`` lives in transformers and
    imports whether or not bitsandbytes is installed at all, so an import proving nothing is not
    a hypothetical. Then bitsandbytes itself is a compiled package that installs cleanly and
    falls back to a CPU only build when it cannot find a CUDA runtime it was built against, so
    importing it does not prove there is a GPU backend either. The only answer that settles it is
    the backend the extension actually loaded.
    """
    try:
        import bitsandbytes
    except ImportError:
        return (
            "bitsandbytes is not installed in this runtime's environment. Repair the Python "
            "runtime from the Local model panel, which installs it from the committed lockfile."
        )
    except Exception as error:  # noqa: BLE001 - a compiled package can fail in its own ways
        return f"bitsandbytes is installed and could not be loaded: {type(error).__name__}: {error}"

    if not torch.cuda.is_available():
        return (
            "there is no CUDA device here, and four bit weights are produced on the card. "
            "This machine can hold a stage at full precision instead."
        )

    try:
        from bitsandbytes.cextension import BNB_BACKEND
    except Exception:  # noqa: BLE001 - an older layout is not a reason to refuse outright
        # The name moved between releases. Not finding it says nothing about the backend, so
        # this falls through to letting the load itself answer rather than guessing.
        return None

    if str(BNB_BACKEND).upper() != "CUDA":
        return (
            f"bitsandbytes loaded its {BNB_BACKEND} backend rather than CUDA, so it cannot "
            "quantize on this card. That usually means the installed build does not match the "
            f"CUDA version torch was built for, which here is {torch.version.cuda}."
        )

    return None


def _best_device() -> str:
    """The accelerator if there is one, and system memory if there is not.

    Falling back to the processor rather than refusing is deliberate. A machine without a usable
    card can still hold a stage, slowly, and slowly is what a pipeline of one fast machine and
    one slow one already is. Refusing would turn a working arrangement into no arrangement.
    """
    if torch.cuda.is_available():
        return f"cuda:{torch.cuda.current_device()}"

    return "cpu"
