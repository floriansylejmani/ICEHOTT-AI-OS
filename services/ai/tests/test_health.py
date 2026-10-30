from fastapi.testclient import TestClient
from app.main import app


def test_live_is_healthy():
    response = TestClient(app).get("/health/live")
    assert response.status_code == 200
    assert response.json() == {
        "status": "healthy",
        "service": "ai",
        "phase": "Phase 3",
    }


def test_no_unprotected_ai_completion_route():
    client = TestClient(app)
    for route in ("/chat", "/v1/chat/completions", "/tools/execute"):
        assert client.post(route).status_code == 404
