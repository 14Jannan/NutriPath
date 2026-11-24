"""Tests for the embedding worker.

The default suite uses a fake encoder, so it needs neither torch nor a model
download. Set NUTRIPATH_LIVE_MODEL_TESTS=1 to also run checks against the real
intfloat/multilingual-e5-small model (downloads about 470 MB on first run).
"""

import json
import math
import os
import threading
import urllib.error
import urllib.request
from http.server import ThreadingHTTPServer

import pytest

import server
from server import E5_SMALL, MINILM, EmbeddingEngine, RequestError, Settings, make_handler


class FakeEncoder:
    """Deterministic unit vectors; records the exact strings it was asked to encode."""

    def __init__(self, dimensions=384, normalized=True):
        self.dimensions = dimensions
        self.normalized = normalized
        self.calls = []

    def encode(self, texts, normalize_embeddings=True):
        self.calls.append(list(texts))
        vectors = []
        for text in texts:
            raw = [((hash(text) >> (i % 32)) & 0xFF) + 1.0 for i in range(self.dimensions)]
            norm = math.sqrt(sum(x * x for x in raw)) if self.normalized else 1.0
            vectors.append([x / norm for x in raw])
        return vectors


def make_engine(encoder=None, **env):
    encoder = encoder or FakeEncoder()
    settings = Settings.from_env(env)
    return EmbeddingEngine(settings, loader=lambda _name, _settings: encoder), encoder


def test_defaults_to_multilingual_e5_with_minilm_as_legacy():
    settings = Settings.from_env({})
    assert settings.default_model == E5_SMALL
    assert settings.allowed_models == (E5_SMALL, MINILM)
    assert settings.dimensions == 384
    assert settings.host == "127.0.0.1"


def test_rejects_unknown_models_in_configuration():
    with pytest.raises(ValueError):
        Settings.from_env({"NUTRIPATH_EMBEDDING_MODELS": "some/unknown-model"})


@pytest.mark.parametrize("input_type,prefix", [("query", "query: "), ("passage", "passage: ")])
def test_e5_inputs_get_the_required_prefix(input_type, prefix):
    engine, encoder = make_engine()
    engine.embed(E5_SMALL, input_type, ["How much protein do I need?"])
    assert encoder.calls == [[prefix + "How much protein do I need?"]]


def test_e5_prefix_is_applied_to_tamil_and_sinhala_text():
    engine, encoder = make_engine()
    tamil = "எனக்கு தினமும் எவ்வளவு புரதம் தேவை?"
    sinhala = "මට දිනකට කොපමණ ප්‍රෝටීන් අවශ්‍යද?"
    engine.embed(E5_SMALL, "query", [tamil, sinhala])
    assert encoder.calls == [["query: " + tamil, "query: " + sinhala]]


def test_minilm_gets_no_prefix():
    engine, encoder = make_engine()
    engine.embed(MINILM, "query", ["protein"])
    assert encoder.calls == [["protein"]]


def test_parse_normalizes_to_nfc_and_keeps_sinhala_zero_width_joiner():
    decomposed = "é"  # e + combining acute
    sinhala = "ප්‍රෝටීන්"
    model, input_type, texts = EmbeddingEngine.parse(json.dumps(
        {"model": E5_SMALL, "inputType": "query", "texts": [decomposed, sinhala]}).encode())
    assert (model, input_type) == (E5_SMALL, "query")
    assert texts[0] == "é"
    assert "‍" in texts[1]


def test_input_type_defaults_to_passage_for_backward_compatibility():
    _, input_type, _ = EmbeddingEngine.parse(json.dumps({"model": MINILM, "texts": ["x"]}).encode())
    assert input_type == "passage"


@pytest.mark.parametrize("body", [
    b"not json",
    b"\xff\xfe",
    json.dumps([]).encode(),
    json.dumps({"texts": ["x"]}).encode(),
    json.dumps({"model": E5_SMALL, "inputType": "document", "texts": ["x"]}).encode(),
    json.dumps({"model": E5_SMALL, "texts": []}).encode(),
    json.dumps({"model": E5_SMALL, "texts": ["x"] * 33}).encode(),
    json.dumps({"model": E5_SMALL, "texts": [""]}).encode(),
    json.dumps({"model": E5_SMALL, "texts": ["   "]}).encode(),
    json.dumps({"model": E5_SMALL, "texts": [1]}).encode(),
    json.dumps({"model": E5_SMALL, "texts": ["a" * 8001]}).encode(),
    json.dumps({"model": E5_SMALL, "texts": ["bad\u0000text"]}).encode(),
])
def test_invalid_requests_are_rejected(body):
    with pytest.raises(RequestError) as error:
        EmbeddingEngine.parse(body)
    assert error.value.status == 400


def test_models_outside_the_allowlist_are_rejected():
    engine, _ = make_engine(NUTRIPATH_EMBEDDING_MODELS=E5_SMALL)
    with pytest.raises(RequestError) as error:
        engine.embed(MINILM, "query", ["x"])
    assert error.value.status == 400


def test_wrong_dimensions_are_reported_as_unavailable():
    engine, _ = make_engine(FakeEncoder(dimensions=768))
    with pytest.raises(RequestError) as error:
        engine.embed(E5_SMALL, "query", ["x"])
    assert error.value.status == 503


def test_unnormalized_vectors_are_reported_as_unavailable():
    engine, _ = make_engine(FakeEncoder(normalized=False))
    with pytest.raises(RequestError) as error:
        engine.embed(E5_SMALL, "query", ["x"])
    assert error.value.status == 503


def test_ready_reflects_model_health():
    assert make_engine()[0].ready()
    assert not make_engine(FakeEncoder(dimensions=10))[0].ready()


@pytest.fixture()
def http_server():
    engine, encoder = make_engine()
    httpd = ThreadingHTTPServer(("127.0.0.1", 0), make_handler(engine))
    thread = threading.Thread(target=httpd.serve_forever, daemon=True)
    thread.start()
    yield f"http://127.0.0.1:{httpd.server_address[1]}", encoder
    httpd.shutdown()
    httpd.server_close()


def post(url, payload):
    data = json.dumps(payload, ensure_ascii=False).encode("utf-8")
    request = urllib.request.Request(url, data=data, headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(request, timeout=10) as response:
            return response.status, json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as error:
        return error.code, json.loads(error.read().decode("utf-8"))


def get(url):
    try:
        with urllib.request.urlopen(url, timeout=10) as response:
            return response.status, json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as error:
        return error.code, json.loads(error.read().decode("utf-8"))


def test_http_embed_returns_model_input_type_and_vectors(http_server):
    url, encoder = http_server
    status, body = post(url + "/embed", {
        "model": E5_SMALL, "inputType": "query", "texts": ["மட்டன்", "මාළු", "fish"]})
    assert status == 200
    assert body["model"] == E5_SMALL
    assert body["inputType"] == "query"
    assert body["dimensions"] == 384
    assert len(body["vectors"]) == 3
    assert encoder.calls[-1][0] == "query: மட்டன்"


def test_http_errors_are_json_and_do_not_echo_text(http_server):
    url, _ = http_server
    status, body = post(url + "/embed", {"model": "other/model", "texts": ["private question"]})
    assert status == 400
    assert "private question" not in json.dumps(body)


def test_http_health_and_ready(http_server):
    url, _ = http_server
    status, health = get(url + "/health")
    assert status == 200
    assert health["defaultModel"] == E5_SMALL
    assert {m["name"] for m in health["models"]} == {E5_SMALL, MINILM}
    assert get(url + "/ready") == (200, {"ready": True, "model": E5_SMALL})


def test_http_unknown_paths_return_404(http_server):
    url, _ = http_server
    assert get(url + "/nope")[0] == 404
    assert post(url + "/other", {})[0] == 404


live = pytest.mark.skipif(os.environ.get("NUTRIPATH_LIVE_MODEL_TESTS") != "1",
                          reason="set NUTRIPATH_LIVE_MODEL_TESTS=1 to load the real model")


@live
def test_live_e5_cross_lingual_questions_are_closer_to_the_protein_passage():
    engine = EmbeddingEngine(Settings.from_env({"NUTRIPATH_MODEL_CACHE": os.environ.get("NUTRIPATH_MODEL_CACHE", "")}))
    passages = engine.embed(E5_SMALL, "passage", [
        "Protein supports muscle growth and repair. Adults need protein every day from foods such as dhal, fish and eggs.",
        "Drinking enough water keeps you hydrated, especially in hot weather.",
    ])
    questions = engine.embed(E5_SMALL, "query", [
        "How much protein do I need?",
        "எனக்கு தினமும் எவ்வளவு புரதம் தேவை?",
        "මට දිනකට කොපමණ ප්‍රෝටීන් අවශ්‍යද?",
    ])
    for question in questions:
        protein = sum(a * b for a, b in zip(question, passages[0]))
        water = sum(a * b for a, b in zip(question, passages[1]))
        assert protein > water
