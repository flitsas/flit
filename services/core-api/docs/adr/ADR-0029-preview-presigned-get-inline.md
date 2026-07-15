# ADR-0029 — Estrategia de preview de documentos: presigned GET URL con Content-Disposition inline

**Estado:** Propuesto  
**Fecha:** 2026-07-14  
**Autor:** architecture-agent (Feature #10701)  
**Feature:** #10701 [TRAMITES] - Visualización y Expediente Documental

## Contexto

R18 requiere visualizar documentos (PDF/imagen) inline sin forzar descarga. Los adjuntos viven en S3 vía file-manager. El API ya soporta subida presigned y lectura de bytes, pero no URL de visualización firmada. Proxyar bytes desde el API no escala y complica autenticación en `<iframe>`.

## Decisión

Extender `IAttachmentStorage` con `GetPresignedViewUrlAsync`. `FileManagerAttachmentStorage` solicita al file-manager una URL firmada S3 con `response-content-disposition=inline` (TTL ~10 min). El endpoint valida tenant + ownership antes de emitir `{ url, expiresAt }`. El FE abre la URL en `DocumentPreviewModal`.

## Alternativas consideradas

- **A — Proxy stream inline:** descartada (bytes por API; iframe sin Bearer).
- **B — One-time token + redirect:** descartada (Redis/cache, over-engineering).
- **C — Presigned GET inline:** elegida (cero bytes por API, alineada a upload presigned).

## Consecuencias

- Preview no consume bandwidth del API.
- Dependencia de soporte `disposition=inline` en file-manager.
- No loguear URL completa (firma HMAC). Ownership check obligatorio antes de emitir.

## Supersedes

Ninguno.
