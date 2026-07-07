# Plan de implementación — Sello de validación biométrica (firma como texto) en el FUR

- **Fecha:** 2026-07-03
- **HU asociada:** [#10488](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/10488) — `[BACKEND] – Trámites – Sellar la validación biométrica (firma como texto) en el FUR desde el certificado Kyverum`
- **Feature padre:** #10453 — Completar la generación documental (FUR + Expediente Consolidado)
- **Sprint:** Sprint 2 · **SP:** 5 · **Estado al momento del plan:** New (pendiente de activación — gate humano)
- **Reporte base:** [`reporte-firmas-fur-2026-07-03.md`](./reporte-firmas-fur-2026-07-03.md)

---

## 1. Decisiones tomadas

| Punto | Decisión |
|---|---|
| Enfoque de firma | **Firma como texto** (sello). No firma como imagen. |
| Origen del hash | **`firmaSerie` del webhook de Kyverum** (`KyverumWebhookSubject.firmaSerie`). Sin spike previo. |
| Modelo de datos en el FUR | **Campo nuevo `SellosIdentidad`** en `FurDocumentData` (no se reutiliza `SellosFirma`). |

Racional del campo separado: la firma electrónica de compraventa (`ProcedureInstanceSignature`, mock, solo traspaso) y la validación biométrica de identidad (`ProcedureInstanceBiometricValidation`, Kyverum) son conceptos distintos; mezclarlos en `SellosFirma` acopla dos flujos que hoy están separados.

---

## 2. Validación del código existente

**La obtención del certificado de validación biométrica YA está implementada y es robusta.** No se construye desde cero.

| Pieza | Ubicación | Función |
|---|---|---|
| Cliente de validación | `Flit.Infrastructure/Kyverum/KyverumVerifyClient.cs` | `POST /v1/validations` (inicia), `GET /v1/validations/{id}` (estado) |
| Cliente de certificado | `Flit.Infrastructure/Kyverum/KyverumCertificateClient.cs` | `GET /v1/validations/{id}/certificado` → descarga el PDF |
| Persistencia | `Flit.Tramites.Domain/Entities/ProcedureInstanceBiometricValidation.cs` | `KyverumVerificationId` (uuid), `ValidatedAt` (fecha aprobación), `ValidUntil` (fecha vencimiento), `Score`, `ProviderStatus` |
| Certificado en el expediente | `FurCommand.cs:220-267` (`TryDownloadIdentityCertificateAsync`) | Descarga el PDF real de Kyverum y lo incrusta como adjunto `certificado_identidad` |
| Webhook + reconciliación | `KyverumVerifyCommand.cs`, workers de reintento/reconcile | Recibe resultado, HMAC, reintentos, dead-letter |
| Auditoría / observabilidad | `IdentityValidationAuditEvent.cs` (HU #87) | Una fila por paso del ciclo |

### 2.1 ⚠️ Discrepancia crítica de API (para pruebas manuales)

El `curl` de referencia usa una **API distinta** a la que integra el código:

| `curl` manual (Postman) | Código FLIT |
|---|---|
| `POST /admin/api/login` + cookie `kv_admin` | ❌ No se usa (obsoleto) |
| `GET /admin/api/validations/{id}/certificado` con cookie | `GET /v1/validations/{id}/certificado` con `Authorization: Bearer <ApiKey>` |
| Auth por sesión/cookie admin | Auth por **API key Bearer** (`KyverumOptions.ApiKey`) |

Por eso las pruebas manuales contra el admin API fallan: **el flujo productivo no requiere login por cookie.** Para probar manualmente, usar `/v1/...` con `Authorization: Bearer <KYVERUM_API_KEY>`, o los endpoints internos de FLIT (`GET /api/v1/tramites/instances/{id}/biometric/{validationId}/certificado`).

---

## 3. Gap a cerrar

1. **El hash no se persiste.** Se guardan uuid, fechas y score, pero no el hash. El webhook trae el campo candidato `firmaSerie` (`KyverumVerifyCommand.cs:531`), hoy ignorado.
2. **La validación biométrica no alimenta el sello del FUR.** `SellosFirma` (`FurCommand.cs:169-172`) se construye solo desde `ProcedureInstanceSignature`. La `ProcedureInstanceBiometricValidation` solo controla el gate `IdentidadValidada` (que pinta "NO FIRMADO"); no aporta texto al recuadro de firma.

---

## 4. Plan por fases

### Fase 1 — Persistir el hash del certificado
- Nueva columna `certificate_hash` (o `firma_serie`) en `ProcedureInstanceBiometricValidation` — migración + config EF + DDL.
- Capturar `firmaSerie` en `KyverumWebhookHandler` (`KyverumVerifyCommand.cs`) y en la vía de reconciliación.
- Mantener la sanitización PII vigente (no persistir `datosExtraidos`).

**Archivos:**
- `Flit.Tramites.Domain/Entities/ProcedureInstanceBiometricValidation.cs`
- `Flit.Infrastructure/Persistence/Configurations/Tramites/ProcedureInstanceBiometricValidationConfiguration.cs`
- `Flit.Infrastructure/Persistence/Migrations/…_AddBiometricCertificateHash.cs` (nueva)
- `Flit.Infrastructure/Persistence/Sql/Ddl/17-tramites-kyverum.sql`
- `Flit.Tramites.Application/UseCases/ProcedureInstances/KyverumVerifyCommand.cs` (mapeo de `firmaSerie`)

### Fase 2 — Exponer datos biométricos al generador del FUR
- Nuevo campo `SellosIdentidad` en `FurDocumentData`.
- En `GenerarFurHandler.AssembleData`, construir el sello por parte desde la validación biométrica aprobada+vigente (uuid, hash, `ValidatedAt`, `ValidUntil`). El handler ya carga la biométrica vía `GetByIdWithBiometricsAndActorsAsync`.

**Archivos:**
- `Flit.Tramites.Application/Documents/IFurDocumentGenerator.cs` (record `FurDocumentData`)
- `Flit.Tramites.Application/UseCases/ProcedureInstances/FurCommand.cs`

### Fase 3 — Render del sello en el FUR
- En `FurFieldMapper`, pintar `SellosIdentidad` en `vehicle_owner_signature` (propietario/vendedor) y `vehicle_buyer_signature` (comprador).
- **Sin cambios de coordenadas** → no toca `fur-field-manifest.json` ni la línea base de `FurManifestGuardTests`.
- Respetar el override "NO FIRMADO" cuando `!IdentidadValidada` (`FurFieldMapper.cs:100-107`).
- La caja es `335×30 pt, fontSize 6, multiline` (~3 líneas). Formato compacto propuesto:
  ```
  Validación biométrica CC 1234567890
  Cert: a3f8…9c1  Aprob: 03/07/2026
  Vence: 02/08/2026
  ```

**Archivos:**
- `Flit.Infrastructure/Documents/Fur/FurFieldMapper.cs`

### Fase 4 — Pruebas
- Unit tests del armado de `SellosIdentidad` (aprobada / rechazada / sin validar).
- Test de render que verifique el texto en el campo de firma.
- Verificar que `FurManifestGuardTests` sigue pasando sin cambios (23 tests).

### Fuera de alcance
- Firma como imagen: `FurDocumentData.FirmaImagenes` es **código muerto** (sin productor). Candidato a eliminar en esta HU o en una de deuda técnica.
- Integración real de firma electrónica (ZapSign).
- Visualización del hash/certificado en el frontend → HU aparte (Fase 5 opcional).

---

## 5. Criterios de aceptación (resumen)

- **AC1** — El `firmaSerie` del webhook se persiste como hash (webhook y reconciliación); se mantiene la sanitización PII.
- **AC2** — `FurDocumentData.SellosIdentidad` contiene, por parte, uuid + hash + fecha aprobación + fecha vencimiento.
- **AC3** — El recuadro de firma muestra el sello del certificado biométrico; el manifest no cambia.
- **AC4** — Si no hay validación aprobada y vigente, sigue mostrándose "NO FIRMADO".
- **AC5** — Pruebas unitarias de armado y render; suite completa en verde.

---

## 6. Mapa de referencias de código

| Concepto | Referencia |
|---|---|
| Construcción actual de `SellosFirma` | `FurCommand.cs:169-172` |
| Record `FurDocumentData` | `IFurDocumentGenerator.cs:46-66` (`SellosFirma`:56, `FirmaImagenes`:59 muerto, `IdentidadValidada`:60-62) |
| Decisión imagen/texto/NO FIRMADO | `FurFieldMapper.cs:112-128` (`SetSignature`), `:100-107` (override), `:266-278` (`SellosTexto`) |
| Campos de firma del manifest | `fur-field-manifest.json:80` (`vehicle_owner_signature`), `:97` (`vehicle_buyer_signature`) |
| Entidad biométrica | `ProcedureInstanceBiometricValidation.cs` (`KyverumVerificationId`:36, `ValidatedAt`:81, `ValidUntil`:90) |
| Campo `firmaSerie` del webhook | `KyverumVerifyCommand.cs:531` (`KyverumWebhookSubject`) |
| Descarga del certificado PDF | `FurCommand.cs:220-267`, `KyverumCertificateClient.cs` |

---

## 7. Estado y próximos pasos

1. **HU #10488 creada en `New`** (Sprint 2, SP 5, asignada a humano). Pendiente el tag `DOR` (el PAT no puede crear tags → `TF401289`; se agrega manualmente).
2. **Gate humano:** la activación (`Active`) y el arranque de implementación por `/implement-story` esperan confirmación explícita ("sí").
