# OCR de trámites — verificación E2E

Guía para verificar el OCR semántico de documentos de trámites (`POST /api/v1/tramites/ocr/{tipo}`)
extremo a extremo. Cubre dos niveles: la verificación **automatizada** (proveedor mock, en CI) y la
verificación **manual con el proveedor real** (Anthropic), que requiere una API key y el stack levantado.

## Qué se verifica automáticamente (mock, sin key)

Sin configuración adicional (`Ocr:Provider=mock`, el valor por defecto), la suite ya cubre:

- **Unit** — `Flit.Tramites.Application.Tests/Ocr/*` (handler, prompts, mock) y
  `Flit.Infrastructure.Tests/Ocr/*` (recorte PDF con PdfSharpCore).
- **Integración E2E backend** — `Flit.Admin.Tests/Ocr/TramitesOcrEndpointTests.cs`: ejerce el endpoint
  por el pipeline HTTP real (WebApplicationFactory) — ruta mapeada, binding multipart, resolución por
  magic bytes, forma de la respuesta `{ ok, tipo, data, extractedPdfBase64 }` y errores (400).
- **Frontend** — `frontend/__tests__/tramites-client-upload.test.ts` (cliente + validaciones tipo/VIN)
  y `document-checklist.test.tsx` (UI del wizard).

```bash
# backend
dotnet test services/core-api/tests/Flit.Tramites.Application.Tests --filter Ocr
dotnet test services/core-api/tests/Flit.Infrastructure.Tests --filter Ocr
dotnet test services/core-api/tests/Flit.Admin.Tests --filter Ocr
# frontend
cd frontend && npx vitest run __tests__/tramites-client-upload.test.ts __tests__/document-checklist.test.tsx
```

## Verificación manual con el proveedor real (Anthropic)

Requiere una `ANTHROPIC_API_KEY` válida y el API levantado. **Paso manual** (no automatizable en CI).

### Configuración

Activar el proveedor real por variables de entorno (12-factor; tienen prioridad sobre appsettings):

```bash
export OCR_PROVIDER=anthropic
export ANTHROPIC_API_KEY=sk-ant-...
# opcionales (valores por defecto):
# export ANTHROPIC_MODEL=claude-haiku-4-5-20251001
# export ANTHROPIC_TIMEOUT_SECONDS=60
# export ANTHROPIC_MAX_TOKENS=2000
```

Equivalente en `appsettings.Development.json`:

```json
"Ocr": { "Provider": "anthropic" },
"Anthropic": { "ApiKey": "sk-ant-...", "Model": "claude-haiku-4-5-20251001", "TimeoutSeconds": 60, "MaxTokens": 2000 }
```

La API key **nunca** se loguea. Sin key (o con `Ocr:Provider=mock`) el endpoint no llama a Anthropic.

### Llamada directa (curl)

Requiere un JWT válido y el header `X-Tenant-Id` (mismo patrón que los adjuntos):

```bash
curl -sS -X POST "http://localhost:5000/api/v1/tramites/ocr/factura" \
  -H "Authorization: Bearer $JWT" \
  -H "X-Tenant-Id: $TENANT_ID" \
  -F "file=@factura.pdf"
```

Respuesta OK: `{ "ok": true, "tipo": "factura", "data": { "es_factura_valida": true, ... }, "extractedPdfBase64": null }`.

### Casos de aceptación

| Caso | Documento | Esperado |
|------|-----------|----------|
| 1. Factura real | Factura de venta de vehículo (PDF/imagen) | `es_factura_valida: true`; en el wizard, badge **Verificado** + resumen; adjunto sube a S3 |
| 2. No es factura | PDF con sólo el texto "FACTURA DE VENTA" (sin datos) | `es_factura_valida: false`; badge **Rechazado**; matrícula sube igual, traspaso no sube |
| 3. PDF multipágina | PDF que mezcla factura + otros docs | Respuesta trae `extractedPdfBase64`; el wizard sube el **recorte**, no el PDF completo |
| 4. Proveedor caído | Cualquiera, con Anthropic inaccesible o sin key | HTTP **503** con mensaje de carga manual; el documento **no** se sube |

Los tipos por modalidad: matrícula → `factura`, `aduana`, `impronta`, `soat`; traspaso → `impronta`, `soat`.
Archivos entre 10 y 20 MB se suben **sin** OCR (se marcan "no analizado" en la UI).
