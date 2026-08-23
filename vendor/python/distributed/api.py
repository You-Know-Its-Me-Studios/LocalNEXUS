"""The OpenAI shaped face of the pipeline, served by the host.

Everything above this file deals in tensors and layer ranges. This file deals in messages and
tokens, and it is the only place that knows the model has a tokenizer, a chat template or a stop
token. That division is what lets a model node in the application talk to a pipeline spread over
four machines using exactly the request it already sends to one.

Only the host runs this. It is stage 0, so a request arriving here is already where it needs to
be to start.

Detokenization is done by decoding the whole answer each time and sending on what is new, rather
than by decoding each token as it arrives. A token is not a character: a word can be split across
two of them and a single emoji across four, and decoding them one at a time produces replacement
characters at every boundary. Decoding the whole thing is quadratic in the length of the answer
and the constant is a few microseconds, which is nothing next to a token that crossed a network.
"""

from __future__ import annotations

import json
import logging
import time
import uuid
from typing import Any

from fastapi import FastAPI, HTTPException
from fastapi.responses import JSONResponse, StreamingResponse
from starlette.requests import Request
from transformers import AutoTokenizer

from .coordinator import Pipeline, PipelineError

_log = logging.getLogger(__name__)

#: What a request gets if it does not say. Long enough to be useful, short enough that a runaway
#: answer over a pipeline does not tie up every machine for an hour.
DEFAULT_MAX_TOKENS = 512


class Api:
    """Turns chat requests into token loops and back again."""

    def __init__(self, pipeline: Pipeline, model_dir: str, model_id: str = "") -> None:
        self._pipeline = pipeline
        self._model_dir = model_dir
        self._model_id = model_id or _name_of(model_dir)

        self._tokenizer = AutoTokenizer.from_pretrained(model_dir)
        self._defaults = _generation_defaults(model_dir)
        self._stop_tokens = _stop_tokens(self._tokenizer, model_dir)

        _log.info("serving %s, stopping on %s", self._model_id, sorted(self._stop_tokens))

    # -- what the pipeline is asked for ------------------------------------------------------

    def _sampling(self, body: dict[str, Any]) -> dict[str, Any]:
        """The settings for one request: what was asked for, over what the model recommends.

        A model ships a generation_config saying how it wants to be sampled, and ignoring it is
        how a model that behaves well everywhere else behaves badly here. It is the floor, and
        anything the request names wins.
        """
        settings = dict(self._defaults)

        for name in ("temperature", "top_p", "top_k", "repetition_penalty"):
            if body.get(name) is not None:
                settings[name] = body[name]

        return settings

    def _max_tokens(self, body: dict[str, Any]) -> int:
        asked = body.get("max_tokens") or body.get("max_completion_tokens")

        return int(asked) if asked else DEFAULT_MAX_TOKENS

    def _prompt_for_chat(self, body: dict[str, Any]) -> list[int]:
        messages = body.get("messages")

        if not isinstance(messages, list) or not messages:
            raise HTTPException(status_code=400, detail="messages is required and cannot be empty")

        try:
            return _as_token_ids(self._tokenizer.apply_chat_template(
                messages, add_generation_prompt=True, tokenize=True
            ))
        except HTTPException:
            raise
        except Exception as error:  # noqa: BLE001 - a template failure is the caller's problem
            raise HTTPException(
                status_code=400, detail=f"the messages could not be templated: {error}"
            ) from error

    def _prompt_for_completion(self, body: dict[str, Any]) -> list[int]:
        prompt = body.get("prompt")

        if isinstance(prompt, list):
            prompt = "".join(str(part) for part in prompt)

        if not isinstance(prompt, str) or not prompt:
            raise HTTPException(status_code=400, detail="prompt is required and cannot be empty")

        return _as_token_ids(self._tokenizer(prompt, add_special_tokens=False))

    # -- running one request -----------------------------------------------------------------

    async def _run(self, tokens: list[int], body: dict[str, Any], outcome: dict[str, Any]):
        """Yields the growing answer as (new text, token id) for each token produced.

        ``outcome`` is filled in as it goes, because why an answer ended is not something the
        caller can work out from what it received: an answer that stopped on its own and one that
        ran into the limit can be the same length.
        """
        produced: list[int] = []
        shown = ""
        outcome["reason"] = "length"

        generator = self._pipeline.generate(
            prompt_tokens=tokens,
            sampling=self._sampling(body),
            max_new_tokens=self._max_tokens(body),
            stop_tokens=self._stop_tokens,
        )

        async for token in generator:
            produced.append(token)

            if token in self._stop_tokens:
                outcome["reason"] = "stop"
                return

            whole = self._tokenizer.decode(produced, skip_special_tokens=True)

            # A token can land mid character. When it does the decoded text does not grow, and
            # the right thing is to wait for the one that completes it rather than to send a
            # replacement character somebody's terminal will render as a box.
            if len(whole) > len(shown):
                fresh, shown = whole[len(shown):], whole
                yield fresh, token

    async def chat(self, request: Request) -> Any:
        body = await _body_of(request)
        tokens = self._prompt_for_chat(body)

        if body.get("stream"):
            return StreamingResponse(
                self._stream(tokens, body, chat=True),
                media_type="text/event-stream",
                headers={"Cache-Control": "no-cache", "X-Accel-Buffering": "no"},
            )

        return JSONResponse(await self._whole(tokens, body, chat=True))

    async def completions(self, request: Request) -> Any:
        body = await _body_of(request)
        tokens = self._prompt_for_completion(body)

        if body.get("stream"):
            return StreamingResponse(
                self._stream(tokens, body, chat=False),
                media_type="text/event-stream",
                headers={"Cache-Control": "no-cache", "X-Accel-Buffering": "no"},
            )

        return JSONResponse(await self._whole(tokens, body, chat=False))

    async def _whole(self, tokens: list[int], body: dict[str, Any], chat: bool) -> dict[str, Any]:
        """The answer in one piece, for a caller that did not ask for a stream."""
        answer: list[str] = []
        outcome: dict[str, Any] = {}
        count = 0

        try:
            async for fresh, _token in self._run(tokens, body, outcome):
                answer.append(fresh)
                count += 1
        except PipelineError as error:
            raise HTTPException(status_code=503, detail=str(error)) from error

        text = "".join(answer)
        finish = outcome.get("reason", "length")

        return {
            "id": _identifier(chat),
            "object": "chat.completion" if chat else "text_completion",
            "created": int(time.time()),
            "model": self._model_id,
            "choices": [
                {"index": 0, "message": {"role": "assistant", "content": text},
                 "finish_reason": finish}
                if chat else
                {"index": 0, "text": text, "finish_reason": finish}
            ],
            "usage": {
                "prompt_tokens": len(tokens),
                "completion_tokens": count,
                "total_tokens": len(tokens) + count,
            },
        }

    async def _stream(self, tokens: list[int], body: dict[str, Any], chat: bool):
        """The answer as server sent events, in the shape the OpenAI clients expect."""
        identifier = _identifier(chat)
        created = int(time.time())
        outcome: dict[str, Any] = {}

        def event(delta: dict[str, Any] | None, finish: str | None) -> str:
            choice: dict[str, Any] = {"index": 0, "finish_reason": finish}

            if chat:
                choice["delta"] = delta or {}
            else:
                choice["text"] = (delta or {}).get("content", "")

            return "data: " + json.dumps({
                "id": identifier,
                "object": "chat.completion.chunk" if chat else "text_completion",
                "created": created,
                "model": self._model_id,
                "choices": [choice],
            }) + "\n\n"

        if chat:
            yield event({"role": "assistant", "content": ""}, None)

        try:
            async for fresh, _token in self._run(tokens, body, outcome):
                yield event({"content": fresh}, None)
        except PipelineError as error:
            # The status line has already gone out with a 200 on it, so a failure part way
            # through cannot be an HTTP error any more. It goes in the stream, because a client
            # that is shown a truncated answer and no reason will treat it as a complete one.
            _log.warning("a streaming request failed part way through: %s", error)

            yield "data: " + json.dumps({"error": {"message": str(error), "type": "pipeline"}}) + "\n\n"
            yield "data: [DONE]\n\n"
            return

        yield event({}, outcome.get("reason", "length"))
        yield "data: [DONE]\n\n"


def create_app(pipeline: Pipeline, model_dir: str, model_id: str = "") -> FastAPI:
    """The host's HTTP face, with the endpoints a model node already knows how to call."""
    api = Api(pipeline, model_dir, model_id)
    app = FastAPI(title="LocalNEXUS distributed inference", docs_url=None, redoc_url=None)

    @app.post("/v1/chat/completions")
    async def chat_completions(request: Request) -> Any:  # noqa: ANN401
        return await api.chat(request)

    @app.post("/v1/completions")
    async def completions(request: Request) -> Any:  # noqa: ANN401
        return await api.completions(request)

    @app.get("/v1/models")
    async def models() -> dict[str, Any]:
        return {
            "object": "list",
            "data": [{"id": api._model_id, "object": "model", "owned_by": "local"}],
        }

    @app.get("/health")
    async def health() -> dict[str, Any]:
        """What the pipeline is, in the terms the Network tab shows machines in.

        The stages are named because a person looking at a pipeline that is answering slowly
        wants to know which machine holds what, and that is not knowable from anywhere else.
        """
        plan = pipeline.plan

        return {
            "status": "ok",
            "model": api._model_id,
            "stages": [
                {
                    "stage": stage.stage_index,
                    "node_id": stage.node_id,
                    "address": stage.address,
                    "layers": [stage.start_layer, stage.end_layer],
                    "holds_embedding": stage.includes_embed,
                    "holds_head": stage.includes_head,
                    "weight_bytes": stage.weight_bytes,
                }
                for stage in plan.stages
            ],
        }

    return app


async def _body_of(request: Request) -> dict[str, Any]:
    try:
        body = await request.json()
    except (json.JSONDecodeError, ValueError) as error:
        raise HTTPException(status_code=400, detail=f"the body is not json: {error}") from error

    if not isinstance(body, dict):
        raise HTTPException(status_code=400, detail="the body has to be an object")

    return body


def _as_token_ids(produced: Any) -> list[int]:
    """A flat list of token ids, out of whatever the tokenizer chose to return.

    A tokenizer answers in three shapes depending on which call was made and which version of
    transformers is installed: a list of ids, a batch holding one list of ids, or a mapping with
    the ids under ``input_ids``. All three mean the same thing, and picking the wrong one is not
    a crash, it is a prompt made of the characters of the word "input_ids".
    """
    if hasattr(produced, "input_ids"):
        produced = produced.input_ids
    elif isinstance(produced, dict):
        produced = produced.get("input_ids", produced)

    if hasattr(produced, "tolist"):
        produced = produced.tolist()

    # A batch of one, which is what every batching call returns for a single prompt.
    while (isinstance(produced, (list, tuple)) and len(produced) == 1
           and isinstance(produced[0], (list, tuple))):
        produced = produced[0]

    if not isinstance(produced, (list, tuple)) or not all(
        isinstance(token, int) for token in produced
    ):
        raise HTTPException(
            status_code=500,
            detail="the tokenizer returned something that is not a list of token ids",
        )

    return [int(token) for token in produced]


def _identifier(chat: bool) -> str:
    return ("chatcmpl-" if chat else "cmpl-") + uuid.uuid4().hex[:24]


def _name_of(model_dir: str) -> str:
    from pathlib import Path

    return Path(model_dir).name


def _generation_defaults(model_dir: str) -> dict[str, Any]:
    """How the model says it wants to be sampled, from the file it says it in.

    A missing generation_config is answered with plain sampling rather than a refusal, because a
    model without one is a model with no preference, not a broken one.
    """
    from pathlib import Path

    settings: dict[str, Any] = {"temperature": 1.0, "top_p": 1.0, "top_k": 0,
                                "repetition_penalty": 1.0}

    path = Path(model_dir) / "generation_config.json"

    if not path.is_file():
        return settings

    try:
        document = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        _log.warning("generation_config.json could not be read and is ignored: %s", error)
        return settings

    for name in ("temperature", "top_p", "top_k", "repetition_penalty"):
        if document.get(name) is not None:
            settings[name] = document[name]

    # A model that says it does not want to be sampled means greedy, and greedy here is a
    # temperature of zero.
    if document.get("do_sample") is False:
        settings["temperature"] = 0.0

    return settings


def _stop_tokens(tokenizer: Any, model_dir: str) -> set[int]:
    """Every token that ends an answer, gathered from everywhere the model states one.

    A model states this in more than one place and they do not always agree. This one names two
    in its generation_config, one for the end of a turn and one for the end of text, while its
    tokenizer names a single ``eos_token_id``. Honouring only the tokenizer leaves the other to
    be printed as text at the end of every answer.
    """
    from pathlib import Path

    stated: Any = None
    path = Path(model_dir) / "generation_config.json"

    if path.is_file():
        try:
            stated = json.loads(path.read_text(encoding="utf-8")).get("eos_token_id")
        except (OSError, json.JSONDecodeError):
            stated = None

    found: set[int] = set()

    for source in (getattr(tokenizer, "eos_token_id", None), stated):
        if isinstance(source, int):
            found.add(source)
        elif isinstance(source, (list, tuple)):
            found.update(int(token) for token in source if isinstance(token, int))

    return found
