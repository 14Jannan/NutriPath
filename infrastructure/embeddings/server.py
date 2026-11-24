"""Loopback-only sentence embedding worker for NutriPath.

Serves normalized 384-dimensional embeddings for the knowledge retrieval
pipeline. The default model is intfloat/multilingual-e5-small (English, Tamil
and Sinhala). sentence-transformers/all-MiniLM-L6-v2 stays available as a
legacy model for regression tests and rollback.

E5 models require an input prefix: "query: " for user questions and
"passage: " for knowledge documents. The caller sends the raw text plus an
``inputType`` and this service adds the prefix that the requested model needs,
so query and document vectors are never produced with the wrong format.

Endpoints
  POST /embed   {"model": str, "inputType": "query"|"passage", "texts": [str]}
  GET  /health  liveness and the models this worker can serve
  GET  /ready   200 only when the default model is loaded and produces valid vectors

Configuration (environment variables)
  NUTRIPATH_EMBEDDING_HOST         bind address, default 127.0.0.1
  NUTRIPATH_EMBEDDING_PORT         port, default 8092
  NUTRIPATH_EMBEDDING_MODEL        default model, loaded at startup
  NUTRIPATH_EMBEDDING_MODELS       comma-separated allowlist (default: both known models)
  NUTRIPATH_EMBEDDING_DIMENSIONS   expected vector size, default 384
  NUTRIPATH_MODEL_CACHE            local model cache folder (default: Hugging Face cache)
  NUTRIPATH_EMBEDDING_DEVICE       torch device, default cpu

Request text is never logged: questions can contain private health details.
"""

from __future__ import annotations

import json
import math
import os
import threading
import unicodedata
from dataclasses import dataclass
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Callable, Protocol, Sequence

E5_SMALL = "intfloat/multilingual-e5-small"
MINILM = "sentence-transformers/all-MiniLM-L6-v2"

MAX_BODY = 512 * 1024
MAX_TEXTS = 32
MAX_TEXT_CHARS = 8000
SOCKET_TIMEOUT_SECONDS = 30
NORM_TOLERANCE = 1e-3
INPUT_TYPES = ("query", "passage")


@dataclass(frozen=True)
class ModelProfile:
    """How a model expects its input to be formatted."""

    name: str
    dimensions: int
    query_prefix: str = ""
    passage_prefix: str = ""

    def format(self, text: str, input_type: str) -> str:
        prefix = self.query_prefix if input_type == "query" else self.passage_prefix
        return prefix + text


KNOWN_MODELS = {
    E5_SMALL: ModelProfile(E5_SMALL, 384, query_prefix="query: ", passage_prefix="passage: "),
    MINILM: ModelProfile(MINILM, 384),
}


class Encoder(Protocol):
    def encode(self, texts: Sequence[str], normalize_embeddings: bool = ...) -> Sequence[Sequence[float]]: ...


class RequestError(Exception):
    def __init__(self, status: int, message: str):
        super().__init__(message)
        self.status = status
        self.message = message


@dataclass(frozen=True)
class Settings:
    host: str
    port: int
    default_model: str
    allowed_models: tuple[str, ...]
    dimensions: int
    cache_folder: str | None
    device: str

    @staticmethod
    def from_env(env: dict[str, str] | None = None) -> "Settings":
        env = dict(os.environ if env is None else env)
        default_model = env.get("NUTRIPATH_EMBEDDING_MODEL", E5_SMALL).strip()
        allowed = tuple(
            m.strip()
            for m in env.get("NUTRIPATH_EMBEDDING_MODELS", f"{E5_SMALL},{MINILM}").split(",")
            if m.strip()
        )
        if default_model not in allowed:
            allowed = (default_model, *allowed)
        unknown = [m for m in allowed if m not in KNOWN_MODELS]
        if unknown:
            raise ValueError(f"Unsupported embedding model(s): {', '.join(unknown)}")
        return Settings(
            host=env.get("NUTRIPATH_EMBEDDING_HOST", "127.0.0.1"),
            port=int(env.get("NUTRIPATH_EMBEDDING_PORT", "8092")),
            default_model=default_model,
            allowed_models=allowed,
            dimensions=int(env.get("NUTRIPATH_EMBEDDING_DIMENSIONS", "384")),
            cache_folder=env.get("NUTRIPATH_MODEL_CACHE") or None,
            device=env.get("NUTRIPATH_EMBEDDING_DEVICE", "cpu"),
        )


def load_sentence_transformer(name: str, settings: Settings) -> Encoder:
    from sentence_transformers import SentenceTransformer  # imported lazily so tests don't need torch

    return SentenceTransformer(
        name, device=settings.device, cache_folder=settings.cache_folder, trust_remote_code=False
    )


class EmbeddingEngine:
    """Validates requests, applies model prefixes and checks every vector it returns."""

    def __init__(self, settings: Settings, loader: Callable[[str, Settings], Encoder] = load_sentence_transformer):
        self.settings = settings
        self._loader = loader
        self._models: dict[str, Encoder] = {}
        self._load_lock = threading.Lock()
        # SentenceTransformer.encode is not documented as thread-safe; one encode at a time.
        self._encode_lock = threading.Lock()

    def model(self, name: str) -> Encoder:
        if name not in self.settings.allowed_models:
            raise RequestError(400, "Unsupported embedding model")
        with self._load_lock:
            if name not in self._models:
                self._models[name] = self._loader(name, self.settings)
            return self._models[name]

    def loaded(self, name: str) -> bool:
        return name in self._models

    @staticmethod
    def parse(raw: bytes) -> tuple[str, str, list[str]]:
        try:
            request = json.loads(raw.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError) as error:
            raise RequestError(400, "Request body must be UTF-8 JSON") from error
        if not isinstance(request, dict):
            raise RequestError(400, "Request body must be a JSON object")
        model = request.get("model")
        input_type = request.get("inputType", "passage")
        texts = request.get("texts")
        if not isinstance(model, str) or not model:
            raise RequestError(400, "model is required")
        if input_type not in INPUT_TYPES:
            raise RequestError(400, "inputType must be 'query' or 'passage'")
        if not isinstance(texts, list) or not 1 <= len(texts) <= MAX_TEXTS:
            raise RequestError(400, f"texts must contain 1 to {MAX_TEXTS} strings")
        cleaned = []
        for text in texts:
            if not isinstance(text, str):
                raise RequestError(400, "Every text must be a string")
            # NFC keeps Tamil and Sinhala combining marks in canonical order. Zero-width
            # joiners are kept: Sinhala uses U+200D for conjuncts such as "ප්‍ර".
            text = unicodedata.normalize("NFC", text).strip()
            if not 1 <= len(text) <= MAX_TEXT_CHARS:
                raise RequestError(400, f"Every text must have 1 to {MAX_TEXT_CHARS} characters")
            if any(unicodedata.category(c) == "Cc" and c not in "\n\t" for c in text):
                raise RequestError(400, "Texts must not contain control characters")
            cleaned.append(text)
        return model, input_type, cleaned

    def embed(self, model_name: str, input_type: str, texts: list[str]) -> list[list[float]]:
        profile = KNOWN_MODELS[model_name] if model_name in KNOWN_MODELS else None
        if profile is None or model_name not in self.settings.allowed_models:
            raise RequestError(400, "Unsupported embedding model")
        encoder = self.model(model_name)
        formatted = [profile.format(text, input_type) for text in texts]
        with self._encode_lock:
            raw = encoder.encode(formatted, normalize_embeddings=True)
        vectors = [[float(x) for x in vector] for vector in raw]
        expected = self.settings.dimensions
        for vector in vectors:
            if len(vector) != expected or len(vector) != profile.dimensions:
                raise RequestError(503, "Embedding model returned an unexpected dimension")
            if not all(math.isfinite(x) for x in vector):
                raise RequestError(503, "Embedding model returned a non-finite value")
            norm = math.sqrt(sum(x * x for x in vector))
            if abs(norm - 1.0) > NORM_TOLERANCE:
                raise RequestError(503, "Embedding model returned an unnormalized vector")
        return vectors

    def ready(self) -> bool:
        try:
            self.embed(self.settings.default_model, "query", ["readiness check"])
            return True
        except Exception:  # noqa: BLE001 - readiness must never crash the worker
            return False


def make_handler(engine: EmbeddingEngine) -> type[BaseHTTPRequestHandler]:
    class EmbeddingHandler(BaseHTTPRequestHandler):
        timeout = SOCKET_TIMEOUT_SECONDS
        protocol_version = "HTTP/1.1"

        def _send(self, status: int, payload: dict) -> None:
            body = json.dumps(payload, allow_nan=False, ensure_ascii=False).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self) -> None:  # noqa: N802 - http.server naming
            if self.path == "/health":
                self._send(200, {
                    "status": "ok",
                    "defaultModel": engine.settings.default_model,
                    "dimensions": engine.settings.dimensions,
                    "models": [
                        {"name": name, "loaded": engine.loaded(name), "dimensions": KNOWN_MODELS[name].dimensions}
                        for name in engine.settings.allowed_models
                    ],
                })
            elif self.path == "/ready":
                ready = engine.ready()
                self._send(200 if ready else 503, {"ready": ready, "model": engine.settings.default_model})
            else:
                self._send(404, {"error": "Not found"})

        def do_POST(self) -> None:  # noqa: N802
            if self.path != "/embed":
                self._send(404, {"error": "Not found"})
                return
            try:
                length = int(self.headers.get("Content-Length", "0"))
            except ValueError:
                length = 0
            if not 0 < length <= MAX_BODY:
                self._send(413 if length > MAX_BODY else 400, {"error": "Invalid Content-Length"})
                return
            try:
                model, input_type, texts = engine.parse(self.rfile.read(length))
                vectors = engine.embed(model, input_type, texts)
            except RequestError as error:
                self._send(error.status, {"error": error.message})
                return
            except Exception:  # noqa: BLE001 - never leak internals or request text
                self._send(503, {"error": "Embedding worker unavailable"})
                return
            self._send(200, {
                "model": model,
                "inputType": input_type,
                "dimensions": len(vectors[0]),
                "vectors": vectors,
            })

        def log_message(self, *_args) -> None:
            # Requests can contain private user questions.
            pass

    return EmbeddingHandler


def main() -> None:
    settings = Settings.from_env()
    engine = EmbeddingEngine(settings)
    engine.model(settings.default_model)  # download/load before accepting traffic
    if not engine.ready():
        raise SystemExit("Default embedding model failed its readiness check")
    with ThreadingHTTPServer((settings.host, settings.port), make_handler(engine)) as server:
        print(
            f"NutriPath embedding worker ({settings.default_model}) listening on {settings.host}:{settings.port}",
            flush=True,
        )
        server.serve_forever()


if __name__ == "__main__":
    main()
