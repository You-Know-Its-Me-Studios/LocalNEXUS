"""What a safetensors model is made of, layer by layer.

A safetensors repository already carries everything needed to split a model across machines,
which is why no splitter tool exists here and none is wanted. ``model.safetensors.index.json``
maps every tensor to the shard holding it, tensor names carry their layer number, and each
shard's own header carries the byte range and dtype of every tensor in it. Reading those two
things answers how many layers there are, what each one weighs, and where each of its tensors
lives, without opening a single weight.

Nothing here loads tensors. It reads two kinds of JSON: the index, and the header at the front
of each shard. For the 30B model that is 18867 entries across 16 files and takes milliseconds,
against 61 GB of weights that are never touched.
"""

from __future__ import annotations

import json
import re
import struct
from dataclasses import dataclass, field
from pathlib import Path

# Tensor names put the layer number in the same place in every architecture transformers
# supports: model.layers.{n}.<the rest>. Anything that does not match is not part of a layer.
_LAYER = re.compile(r"^model\.layers\.(\d+)\.")

# The three tensors that belong to no layer and have to be placed deliberately: the embedding
# runs before the first layer, and the final norm and the head run after the last.
EMBED = "model.embed_tokens.weight"
NORM = "model.norm.weight"
HEAD = "lm_head.weight"

# What a safetensors file starts with: eight bytes of little endian header length, then that
# many bytes of JSON describing every tensor in the file.
_HEADER_LENGTH_BYTES = 8

# A header is metadata and is small. A file claiming a header larger than this is not one of
# ours, and reading it would mean allocating whatever it asked for.
_MAX_HEADER_BYTES = 128 * 1024 * 1024

_DTYPE_BYTES = {
    "BOOL": 1,
    "U8": 1,
    "I8": 1,
    "F8_E4M3": 1,
    "F8_E5M2": 1,
    "U16": 2,
    "I16": 2,
    "F16": 2,
    "BF16": 2,
    "U32": 4,
    "I32": 4,
    "F32": 4,
    "U64": 8,
    "I64": 8,
    "F64": 8,
}


class LayerMapError(Exception):
    """The folder is not a safetensors model this can read, with the reason."""


@dataclass(frozen=True)
class TensorRef:
    """One tensor: what it is called, which shard holds it, and where in that shard."""

    name: str
    shard: str
    dtype: str
    shape: tuple[int, ...]
    begin: int
    end: int

    @property
    def nbytes(self) -> int:
        """How much of the shard this tensor occupies."""
        return self.end - self.begin


@dataclass
class LayerMap:
    """Every tensor of a model, grouped the way a pipeline needs it."""

    model_dir: Path
    layer_count: int

    #: Layer number to the tensors that make it up, every layer present exactly once.
    layers: dict[int, list[TensorRef]] = field(default_factory=dict)

    #: The token embedding, which the first stage runs before its layers.
    embed: TensorRef | None = None

    #: The final norm, which the last stage runs after its layers.
    norm: TensorRef | None = None

    #: The head that turns hidden states into logits, on the last stage.
    head: TensorRef | None = None

    #: Anything matching no rule, kept rather than dropped so a surprise is visible.
    unplaced: list[TensorRef] = field(default_factory=list)

    #: Whether the model reuses its embedding as its head. When it does there is no head tensor
    #: in the checkpoint at all, so the last stage has to hold the embedding to have a head.
    tied_embeddings: bool = False

    def layer_bytes(self, index: int) -> int:
        """What one layer weighs on disk, which is what a stage has to hold for it."""
        return sum(ref.nbytes for ref in self.layers[index])

    @property
    def total_layer_bytes(self) -> int:
        """Every layer together, which is most of the model."""
        return sum(self.layer_bytes(i) for i in self.layers)

    @property
    def embed_bytes(self) -> int:
        """What the first stage carries on top of its layers."""
        return self.embed.nbytes if self.embed else 0

    @property
    def tail_bytes(self) -> int:
        """The final norm and the head, as tensors that exist in the checkpoint."""
        return (self.norm.nbytes if self.norm else 0) + (self.head.nbytes if self.head else 0)

    @property
    def last_stage_extra(self) -> int:
        """What the last stage carries on top of its layers, which is not always the same thing.

        A model with a head of its own carries the norm and the head. A model that ties its head
        to its embedding has no head tensor, so the last stage carries the embedding a second
        time, and that copy is real memory on a real machine. Charging it here is what stops a
        plan from fitting on paper and failing to load.
        """
        tied = self.embed_bytes if self.tied_embeddings and self.head is None else 0

        return self.tail_bytes + tied

    def held_layer_bytes(self, index: int, scale: float) -> int:
        """What one layer occupies once loaded, which is not its size times a constant.

        Quantization applies to some tensors and not others, so a fraction of the whole is the
        wrong arithmetic. See :func:`is_quantizable` for which is which and why it matters more
        for this architecture than for most.
        """
        return sum(_held(ref, scale) for ref in self.layers[index])

    def held_embed_bytes(self, scale: float) -> int:
        """The embedding as loaded. An embedding is a lookup table, so it never quantizes."""
        return _held(self.embed, scale) if self.embed else 0

    def held_last_stage_extra(self, scale: float) -> int:
        """The norm, the head, and a tied embedding if there is one, as loaded."""
        norm = _held(self.norm, scale) if self.norm else 0
        head = _held(self.head, scale) if self.head else 0
        tied = self.held_embed_bytes(scale) if self.tied_embeddings and self.head is None else 0

        return norm + head + tied

    def held_total_bytes(self, scale: float) -> int:
        """The whole model as loaded, which is what has to fit across the machines."""
        return (sum(self.held_layer_bytes(index, scale) for index in self.layers)
                + self.held_embed_bytes(scale)
                + self.held_last_stage_extra(scale)
                + sum(_held(ref, scale) for ref in self.unplaced))

    @property
    def total_bytes(self) -> int:
        """The whole model, which is what has to fit somewhere between the machines."""
        return self.total_layer_bytes + self.embed_bytes + self.tail_bytes + sum(
            ref.nbytes for ref in self.unplaced
        )

    def describe(self) -> str:
        """One paragraph a person can check against what they expected."""
        lines = [
            f"{self.model_dir.name}",
            f"  layers        {self.layer_count}",
            f"  total         {_gb(self.total_bytes)}",
            f"  per layer     {_gb(self.total_layer_bytes // max(1, self.layer_count))} average",
            f"  embedding     {_gb(self.embed_bytes)}",
            f"  norm and head {_gb(self.tail_bytes)}",
        ]

        if self.unplaced:
            lines.append(f"  unplaced      {len(self.unplaced)} tensor(s), {_gb(sum(r.nbytes for r in self.unplaced))}")

        return "\n".join(lines)


def _gb(nbytes: int) -> str:
    return f"{nbytes / (1024 ** 3):.2f} GB"


def _held(ref: TensorRef, scale: float) -> int:
    """What one tensor occupies once loaded, at the given quantization scale."""
    return int(ref.nbytes * scale) if is_quantizable(ref) else ref.nbytes


def is_quantizable(ref: TensorRef) -> bool:
    """Whether this tensor will actually be quantized, rather than merely asked to be.

    This exists because assuming otherwise produced a plan off by a factor of three, and it is
    worth writing down exactly why. Quantization in transformers is a module swap: it walks the
    model and replaces things that are ``nn.Linear`` with a four bit equivalent. A weight that
    does not live in an ``nn.Linear`` is never seen by that walk and stays exactly as it loaded,
    however loudly four bit weights were asked for.

    For most models that distinction is a rounding error, because nearly all the weight is in
    linear projections. For a mixture of experts model it is almost the whole model: transformers
    stacks the experts of a layer into one three dimensional parameter, which is a plain
    ``nn.Parameter`` and not a linear layer at all. On the model this was built for that is a
    hundred and fifty two megabytes of every hundred and sixty one, so quantizing saves about two
    per cent rather than seventy.

    The rule is therefore about which tensors become linear weights, read off names because that
    is all a checkpoint offers:

    * a name carrying ``.experts.`` is an expert, which gets stacked into a parameter
    * ``embed_tokens`` is a lookup table and ``lm_head`` is left alone by default
    * a router gate and every norm are parameters rather than projections
    * anything else two dimensional and ending in ``.weight`` is a projection

    Being wrong in the cautious direction costs a machine. Being wrong the other way is an out of
    memory error part way through a load, so anything unrecognised is treated as not quantized.
    """
    name = ref.name

    if not name.endswith(".weight") or len(ref.shape) != 2:
        return False

    if ".experts." in name:
        return False

    if name in (EMBED, HEAD) or name.endswith("embed_tokens.weight"):
        return False

    # The mixture of experts router. Named gate rather than gate_proj, which is the projection
    # that does quantize, so the two are told apart on the whole component and not a prefix.
    if name.endswith(".mlp.gate.weight"):
        return False

    return True


def build(model_dir: str | Path) -> LayerMap:
    """Reads a safetensors model directory and returns what it is made of.

    Raises:
        LayerMapError: the folder is not a safetensors model, or its index disagrees with the
            shards it names.
    """
    folder = Path(model_dir)

    if not folder.is_dir():
        raise LayerMapError(f"{folder} is not a folder.")

    weight_map = _read_weight_map(folder)
    headers = _read_headers(folder, sorted(set(weight_map.values())))

    layers: dict[int, list[TensorRef]] = {}
    embed = norm = head = None
    unplaced: list[TensorRef] = []

    for name, shard in weight_map.items():
        entry = headers[shard].get(name)

        if entry is None:
            raise LayerMapError(
                f"The index says {name} is in {shard}, and {shard} does not contain it. "
                "The index and the weights are not from the same download."
            )

        ref = _to_ref(name, shard, entry)
        match = _LAYER.match(name)

        if match is not None:
            layers.setdefault(int(match.group(1)), []).append(ref)
        elif name == EMBED:
            embed = ref
        elif name == NORM:
            norm = ref
        elif name == HEAD:
            head = ref
        else:
            unplaced.append(ref)

    if not layers:
        raise LayerMapError(
            "No tensors are named model.layers.N, so this is not a decoder this can split."
        )

    _require_contiguous(layers)

    if head is None and not _ties_embeddings(folder):
        raise LayerMapError(
            f"There is no {HEAD} and the config does not tie the head to the embedding, so "
            "nothing in this checkpoint can turn hidden states into tokens."
        )

    for tensors in layers.values():
        tensors.sort(key=lambda ref: ref.name)

    unplaced.sort(key=lambda ref: ref.name)

    return LayerMap(
        model_dir=folder,
        layer_count=len(layers),
        layers=layers,
        embed=embed,
        norm=norm,
        head=head,
        unplaced=unplaced,
        tied_embeddings=_ties_embeddings(folder),
    )


def _ties_embeddings(folder: Path) -> bool:
    """Whether the model reuses its embedding as its head, according to its own config.

    A missing or unreadable config is answered with no rather than with a refusal, because this
    only ever adds a requirement, and a checkpoint with a head tensor of its own does not care
    either way.
    """
    config = folder / "config.json"

    if not config.is_file():
        return False

    try:
        document = json.loads(config.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return False

    return bool(document.get("tie_word_embeddings", False))


def _require_contiguous(layers: dict[int, list[TensorRef]]) -> None:
    """A gap in the layer numbers means a shard is missing, which is worth saying now.

    Loading would otherwise succeed, place the layers it found, and produce fluent nonsense,
    which is the failure that costs the most to notice.
    """
    expected = set(range(len(layers)))
    found = set(layers)

    if found != expected:
        missing = sorted(expected - found)
        extra = sorted(found - expected)

        raise LayerMapError(
            "The layers are not contiguous from zero. "
            + (f"Missing: {missing}. " if missing else "")
            + (f"Unexpected: {extra}. " if extra else "")
            + "A shard is probably missing from the download."
        )


def _read_weight_map(folder: Path) -> dict[str, str]:
    """Every tensor name to the shard holding it, sharded or not.

    A model small enough to need no sharding has no index and one ``model.safetensors``, so its
    map is built from that file's own header instead. Both shapes are ordinary and neither is
    the special case.
    """
    index = folder / "model.safetensors.index.json"

    if index.is_file():
        try:
            document = json.loads(index.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as error:
            raise LayerMapError(f"{index.name} could not be read: {error}") from error

        weight_map = document.get("weight_map")

        if not isinstance(weight_map, dict) or not weight_map:
            raise LayerMapError(f"{index.name} has no weight_map, so it does not say where anything is.")

        return {str(name): str(shard) for name, shard in weight_map.items()}

    single = folder / "model.safetensors"

    if single.is_file():
        return {name: single.name for name in _read_header(single)}

    raise LayerMapError(
        "There is no model.safetensors.index.json and no model.safetensors, "
        "so there is nothing here to read."
    )


def _read_headers(folder: Path, shards: list[str]) -> dict[str, dict[str, dict]]:
    """The header of every shard the index names."""
    headers: dict[str, dict[str, dict]] = {}

    for shard in shards:
        path = folder / shard

        if not path.is_file():
            raise LayerMapError(f"The index names {shard}, which is not in this folder.")

        headers[shard] = _read_header(path)

    return headers


def _read_header(path: Path) -> dict[str, dict]:
    """Reads one safetensors header, which is all the metadata the file carries.

    Only the front of the file is touched. A shard is gigabytes and its header is kilobytes,
    so this is what makes describing a 61 GB model take milliseconds.
    """
    try:
        with path.open("rb") as handle:
            prefix = handle.read(_HEADER_LENGTH_BYTES)

            if len(prefix) < _HEADER_LENGTH_BYTES:
                raise LayerMapError(f"{path.name} is too short to be a safetensors file.")

            (length,) = struct.unpack("<Q", prefix)

            if length == 0 or length > _MAX_HEADER_BYTES:
                raise LayerMapError(
                    f"{path.name} declares a {length} byte header, which is not a safetensors file."
                )

            body = handle.read(length)

            if len(body) < length:
                raise LayerMapError(f"{path.name} ends inside its own header.")

        document = json.loads(body.decode("utf-8"))
    except LayerMapError:
        raise
    except (OSError, UnicodeDecodeError, json.JSONDecodeError, struct.error) as error:
        raise LayerMapError(f"{path.name} could not be read: {error}") from error

    # __metadata__ is the format's own free text and is not a tensor.
    return {name: entry for name, entry in document.items() if name != "__metadata__"}


def _to_ref(name: str, shard: str, entry: dict) -> TensorRef:
    """One header entry as something the rest of this can use."""
    offsets = entry.get("data_offsets")

    if not isinstance(offsets, list) or len(offsets) != 2:
        raise LayerMapError(f"{name} in {shard} has no usable data_offsets.")

    dtype = str(entry.get("dtype", ""))

    if dtype not in _DTYPE_BYTES:
        raise LayerMapError(f"{name} in {shard} has dtype {dtype!r}, which is not one this reads.")

    shape = tuple(int(dimension) for dimension in entry.get("shape", []))

    return TensorRef(
        name=name,
        shard=shard,
        dtype=dtype,
        shape=shape,
        begin=int(offsets[0]),
        end=int(offsets[1]),
    )
