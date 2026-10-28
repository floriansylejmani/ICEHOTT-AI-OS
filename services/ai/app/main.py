"""ICEHOTT AI OS AI runtime — Phase 0 liveness only.

No user prompts, provider credentials, embeddings or tools are accepted yet.
All workspace authorization will be enforced by the API in Phase 1+.
"""
from fastapi import FastAPI

app = FastAPI(
    title="ICEHOTT AI OS — AI Runtime",
    version="0.1.0",
    docs_url=None,
    redoc_url=None,
)


@app.get("/health/live")
def live() -> dict[str, str]:
    return {"status": "healthy", "service": "ai", "phase": "Phase 0"}
