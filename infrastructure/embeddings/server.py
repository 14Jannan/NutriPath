"""Loopback-only sentence embedding worker for NutriPath."""

import json
from http.server import BaseHTTPRequestHandler, HTTPServer

from sentence_transformers import SentenceTransformer


MODEL = "sentence-transformers/all-MiniLM-L6-v2"
MAX_BODY = 256 * 1024


class EmbeddingHandler(BaseHTTPRequestHandler):
    def do_POST(self):
        if self.path != "/embed":
            self.send_error(404)
            return
        try:
            length = int(self.headers.get("Content-Length", 0))
            if not 0 < length <= MAX_BODY:
                raise ValueError()
            request = json.loads(self.rfile.read(length))
            texts = request["texts"]
            if (
                request["model"] != MODEL
                or not isinstance(texts, list)
                or not 1 <= len(texts) <= 16
                or any(not isinstance(text, str) or not 1 <= len(text) <= 8000 for text in texts)
            ):
                raise ValueError()
        except (KeyError, TypeError, ValueError):
            self.send_error(400, "Invalid embedding request")
            return

        try:
            vectors = self.server.model.encode(texts, normalize_embeddings=True)
            body = json.dumps({"model": MODEL, "vectors": vectors.tolist()}, allow_nan=False).encode()
        except Exception:
            self.send_error(503, "Embedding worker unavailable")
            return
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *_args):
        # Requests can contain private user questions.
        pass


if __name__ == "__main__":
    with HTTPServer(("127.0.0.1", 8092), EmbeddingHandler) as server:
        server.model = SentenceTransformer(MODEL, device="cpu", trust_remote_code=False)
        print("NutriPath embedding worker listening on 127.0.0.1:8092", flush=True)
        server.serve_forever()
