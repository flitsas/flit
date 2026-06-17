"""FastAPI entrypoint for services/python-ml."""

from __future__ import annotations

import structlog
from fastapi import FastAPI
from pydantic import BaseModel, Field

logger = structlog.get_logger(__name__)


class HealthResponse(BaseModel):
    status: str = Field(examples=["ok"])


class OcrCedulaPayload(BaseModel):
    numero: str | None = None
    nombres: str | None = None
    apellidos: str | None = None
    confidence_global: float | None = Field(default=None, alias="confidenceGlobal")

    model_config = {"populate_by_name": True}


class OcrCedulaSuccessResponse(BaseModel):
    cedula: OcrCedulaPayload


class OcrCedulaRequest(BaseModel):
    imagen_base64: str
    mime_type: str = Field(alias="mimeType")

    model_config = {"populate_by_name": True}


def create_app() -> FastAPI:
    """Application factory (hexagonal scaffold — expand adapters over time)."""
    structlog.configure(
        processors=[
            structlog.processors.add_log_level,
            structlog.processors.TimeStamper(fmt="iso"),
            structlog.processors.JSONRenderer(),
        ],
    )

    application = FastAPI(
        title="FLIT Python ML",
        version="1.0.0",
        description="OCR / ML service (MVP scaffold)",
    )

    @application.get("/health", response_model=HealthResponse)
    async def health() -> HealthResponse:
        return HealthResponse(status="ok")

    @application.post("/ocr/cedula:json", response_model=OcrCedulaSuccessResponse)
    async def ocr_cedula_json(
        body: OcrCedulaRequest,
    ) -> OcrCedulaSuccessResponse:
        """Stub MVP: accepts payload; OCR pipeline not wired yet."""
        logger.info(
            "ocr_cedula_stub",
            mime_type=body.mime_type,
            payload_bytes=len(body.imagen_base64),
        )
        return OcrCedulaSuccessResponse(
            cedula=OcrCedulaPayload(
                numero=None,
                nombres=None,
                apellidos=None,
                confidenceGlobal=None,
            ),
        )

    return application


app = create_app()
