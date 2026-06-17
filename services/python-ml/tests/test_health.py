"""Smoke tests for python-ml scaffold."""

from app.main import app
from fastapi.testclient import TestClient

client = TestClient(app)


def test_health_returns_ok() -> None:
    response = client.get("/health")
    assert response.status_code == 200
    assert response.json() == {"status": "ok"}


def test_ocr_cedula_stub_accepts_payload() -> None:
    response = client.post(
        "/ocr/cedula:json",
        json={"imagen_base64": "dGVzdA==", "mimeType": "image/jpeg"},
    )
    assert response.status_code == 200
    body = response.json()
    assert "cedula" in body
