# Plan técnico — Extraer y reusar la firma manuscrita del certificado Kyverum

> **Versión:** v1  
> **Fecha:** 2026-09-02  
> **Estado:** Implementado en `feature/AB-12040-firma-manuscrita-kyverum`. Feature #12039 · HU #12040.  
> **Decisión de diseño acordada:** alternativa **C** — extraer **una vez**, persistir path+hash (como el baúl), reusar; backfill perezoso si el PDF aún no existe al aprobar.  
> **Origen:** conversación 2026-09-02 (certificado de validación de identidad; Kyverum no expone la imagen por API).  
> **ADRs relacionados:** ADR-0025 (baúl, custodia S3), ADR-0031 (firma por identidad en FUR), Bug #11146 (una vía de firma por parte).  
> **ADR a abrir:** `ADR-0054` (Propuesto) — persistencia del recorte + librería de extracción + PII.

---

## 1. Qué se pide

Kyverum **no** entrega la rúbrica por servicio. El trazo vive solo en el PDF del certificado (`GET /v1/validations/{id}/certificado`). FLIT debe:

1. Bajar ese PDF cuando la validación queda **aprobada** (o en el primer download exitoso).
2. Recortar la zona **FIRMA Y AUTORIZACION DE TRAMITE DIGITAL** (trazo entre el párrafo legal y el nombre en mayúsculas).
3. Guardar un PNG en storage (no en PostgreSQL).
4. Estampar esa imagen en FUR / mandato / solicitud virtual / compraventa cuando la parte firma por **sello de validación de identidad** (no por baúl).
5. Reusar el artefacto en trámites posteriores mientras la identidad esté vigente (HU #10350).

**Fallback:** si no hay PNG (PDF ausente, recorte fallido, mock, migración V1), se conserva el sello de texto `BuildIdentidadSello`.

---

## 2. Qué hay hoy (hechos de código)

| Hecho | Dónde |
|---|---|
| El PDF se pide a Kyverum con API key, no se recorta | `KyverumCertificateClient.DownloadCertificateAsync` |
| GET on-demand; 404/502/503 al cliente | `DescargarCertificadoIdentidadHandler` + `BiometricaEndpoints` `.../biometric/{validationId}/certificado` |
| El FUR embebe el PDF completo, best-effort | `FurCommand.TryDownloadIdentityCertificateAsync` |
| Al aprobar: estado, vigencia, `certificate_hash`; **no** hay imagen | `IdentityValidationResultApplier` |
| Outbox `completed` → firma compraventa / FUR **sin** esperar un artefacto de rúbrica | `IdentityValidationOutboxProcessor` → `IdentityValidationCompletedConsumer` |
| Imagen de firma en documentos = **solo baúl** | `ResolveVaultSignaturesAsync` → `FurDocumentData.FirmaImagenes` |
| Identidad = **sello multilínea de texto** | `BuildIdentidadSello` → `SellosIdentidad` |
| Exclusividad baúl vs identidad | `FurCommand` ~264–294, Bug #11146 |
| Custodia de PNG: S3, fila solo path+sha | `SignatureVaultArtifactStorage` + ADR-0025 |
| Prevalidación standalone: `procedure_instance_id` nullable | `ProcedureInstanceBiometricValidation` |
| `IAttachmentStorage.SaveAsync` agrupa por un Guid de “instancia” | El baúl pasa **`tenantId`** (no hay trámite) |

El webhook **no** debe bajar el PDF: hay que mantenerlo delgado y Kyverum a veces no tiene el certificado en el mismo instante que `aprobado` (mismo hueco que `firmaSerie` / HU #11015).

---

## 3. Decisiones de producto / arquitectura (cerradas en v1)

| ID | Decisión | No hacer |
|---|---|---|
| D1 | Extraer **una vez** por validación aprobada Kyverum; persistir `signature_image_path` + `signature_image_sha256` en la fila biométrica | Recortar en cada FUR, cada GET o en el navegador |
| D2 | Bytes en S3 vía `IAttachmentStorage`; tipo `identity_signature`, filename `signature.png`. Agrupar con **`tenantId`** (como el baúl): vale para trámite y standalone | Guardar PNG en jsonb/bytea; clonar el adjunto en cada trámite |
| D3 | Worker de outbox: **extraer ANTES** de `IdentityValidationCompletedConsumer` (auto-FUR). Si el PDF no está, FUR con sello; backfill después **no** regenera documentos solos | Extraer dentro de `IdentityValidationResultApplier` o del HTTP del webhook |
| D4 | Backfill en `DescargarCertificadoIdentidadHandler` y `TryDownloadIdentityCertificateAsync` cuando path vacío y el PDF llega | Recorte en el front |
| D5 | Estampa: **imagen + leyenda** (UUID / serie / fechas). Sello de texto solo si no hay imagen. Baúl sigue ganando (Bug #11146) | Pintar rúbrica y baúl a la vez; sustituir el PDF del certificado |
| D6 | Provider `mock` y `migracion_v1`: no extraen | Inventar una rúbrica QuestPDF |
| D7 | GET certificado sigue devolviendo el **PDF completo** | Cambiar el contrato del visor |

**Abierta (gate experto + PO, no bloquea HU1–HU2):** dictamen `expert-doc-engine` sobre si el recuadro del FUR admite rúbrica + leyenda o debe seguir siendo solo sello. Hasta el dictamen, HU3 no arranca.

---

## 4. Criterios funcionales (insumo Feature ADO)

**CF-01.** Dada una validación Kyverum **aprobada** y un certificado PDF descargable, el sistema persiste un PNG de la rúbrica (path + SHA-256) asociado a **esa** fila biométrica.

**CF-02.** Si Kyverum responde 404 / PDF vacío al aprobar, la aprobación **no** se revierte; se reintenta extracción (outbox + backfill). El sello de texto sigue disponible.

**CF-03.** Un segundo trámite que reusa la identidad vigente **no** vuelve a pedir el recorte a Kyverum si el path ya está.

**CF-04.** Al generar documentos, si la parte firma por identidad y hay PNG, se estampa la imagen; si elige baúl o hay imagen de baúl, no se pinta la rúbrica Kyverum.

**CF-05.** Fallo de recorte o de S3: warning, documentos con sello de texto, sin 500 en FUR.

**CF-06.** El material no viaja en logs ni en DTOs de listado biométrico; columnas con `COMMENT @pii:high`.

---

## 5. Modelo de datos

Tabla existente `tramites.procedure_instance_biometric_validations` (RLS y audit trigger ya existen):

| Columna | Tipo | Notas |
|---|---|---|
| `signature_image_path` | `varchar(1000)` NULL | Path opaco S3. PII alta (A15). |
| `signature_image_sha256` | `varchar(64)` NULL | Hex minúsculas. CHECK: ambos NULL o ambos NOT NULL. |

- **No** hay tabla nueva.  
- **No** hace falta índice de búsqueda: se lee por PK de la validación / reuso ya indexado (`ix_biometric_validations_vigente_approved`).  
- Migración EF Core `Up`/`Down` + mapeo en `ProcedureInstanceBiometricValidationConfiguration`.  
- Idempotencia: si `signature_image_path` no es null, el extractor **no** pisa (salvo HU futura de “forzar re-extracción” — fuera de alcance).

---

## 6. Diseño técnico

### 6.1 Puertos (Application)

```text
IIdentitySignatureExtractor
  TryExtract(byte[] pdf) → IdentitySignatureCrop?   // PNG PNG8/32, o null si no hay anclas

IIdentitySignatureArtifactStorage                 // espejo del baúl, no IAttachmentStorage crudo en handlers
  SaveAsync(tenantId, png) → (path, sha256)
  OpenReadAsync(path) → Stream?

IIdentitySignatureCapture                         // orquesta download + extract + save; idempotente
  EnsureAsync(validation, ct) → captured | skipped | retryable
```

`EnsureAsync` reglas:

- No Kyverum / sin `KyverumVerificationId` → skipped.  
- Path ya presente → skipped.  
- Download null → retryable (PDF aún no listo).  
- Download throw transitorio → retryable.  
- Extract null → skipped definitivo + warning (layout Kyverum cambió).  
- Save OK → escribe path+sha en la entidad; caller `SaveChanges`.

### 6.2 Extracción (Infrastructure) — spike obligatorio en HU1

Orden de intento (el spike elige uno y lo deja en el ADR):

1. **XObject** (PdfPig u homólogo): si Kyverum embebe la rúbrica como imagen, es lo más limpio (sin watermark `BORRADOR`).  
2. Si está aplanada: raster de la página de `FIRMA Y AUTORIZACION…` + recorte por **anclas de texto** (nombre en mayúsculas / `Firma manuscrita, registro de la firma`) + umbral de tinta oscura.

Dependencias: `SixLabors.ImageSharp` ya está versionada en `Directory.Packages.props` pero **no** referenciada en csproj. PdfPig / PDFtoImage serían **paquete nuevo** → justificación en ADR-0054. No añadir ambos “por si acaso”.

**Fixture:** un certificado Kyverum sanitizado (sin PII real de `context/muestras/`) en `tests/.../Fixtures/KyverumCertificate/`. El recorte no debe ser la página entera ni vacío.

### 6.3 Orden en la outbox (crítico)

Hoy `ProcessOneAsync` hace: consumer FUR/firma → `DeferredSignatureBatchConsumer`.

Debe quedar:

```text
1. IdentitySignatureCapture.EnsureAsync   // best-effort; retryable NO aborta el ciclo
2. IdentityValidationCompletedConsumer    // auto-FUR / solicitar firma
3. DeferredSignatureBatchConsumer
```

Si (1) es retryable, **igual** se corre (2): no bloquear radicación documental. El PDF puede aparecer minutos después; el backfill llena el path para el **siguiente** generate. Regenerar FUR ya emitido **no** es v1 (posible HU4).

Reintentos: el ciclo de outbox ya incrementa `attempts`. `EnsureAsync` retryable no debe por sí solo agotar el dead-letter del evento `completed` (el FUR tiene que poder correr). Implementación: captura en try/catch propio; fallo retryable = log, no throw. Un worker de reconcile existente **no** es el sitio (es para estado Kyverum, no para PDF).

Opcional v1.1: 1–2 reintentos cortos (p. ej. 2 s) **dentro** de `EnsureAsync` solo en el primer `aprobado`, para ganar la carrera típica “webhook vs PDF listo” sin alargar el webhook HTTP.

### 6.4 Estampa (HU3)

No meter la PNG de identidad en `FirmaImagenes` sin marca de origen: ese diccionario alimenta **metadatos de baúl** (`FirmaBaulMetadatos`). Mezclarlos haría pasar la rúbrica Kyverum por `FlitFirmaBaulSello`.

Contrato propuesto:

- `FurDocumentData.FirmaIdentidadImagenes: IReadReadOnlyDictionary<string, byte[]>?`  
- Resolver en `FurCommand` junto a `ResolveVaultSignaturesAsync`, **después** de la exclusividad baúl: si el rol sigue en `sellosIdentidad` y hay path, leer PNG y poblar `FirmaIdentidadImagenes`; el sello de texto queda como **leyenda** (mismo `BuildIdentidadSello`) o se compacta a una línea bajo la imagen — lo fija el dictamen.  
- `FlitFirmaBlock` / `FurFieldMapper`: rama `ImagenIdentidad` entre baúl e sello puro.

### 6.5 Front

**Fuera de v1.** El botón de certificado no cambia. No hay preview admin de la rúbrica extraída (el baúl sí tiene preview; se puede copiar después si Producto lo pide).

---

## 7. Diagrama de secuencia

```mermaid
sequenceDiagram
  participant KV as Kyverum
  participant WH as Webhook / reconcile
  participant Applier as ResultApplier
  participant Outbox as identity_validation_outbox
  participant Cap as IdentitySignatureCapture
  participant S3 as AttachmentStorage
  participant Fur as GenerarFurHandler

  WH->>Applier: aprobado + CertificateHash
  Note over Applier: sin I/O de PDF
  Applier->>Outbox: IdentityValidationCompleted
  Outbox->>Cap: EnsureAsync
  Cap->>KV: GET .../certificado
  alt PDF OK y recorte OK
    Cap->>S3: identity_signature/signature.png
    Cap->>Cap: path + sha en fila biométrica
  else 404 / timeout
    Note over Cap: retryable; no throw
  end
  Outbox->>Fur: auto-FUR (imagen si ya hay path; si no, sello)
  Note over Fur: GET certificado o FUR posterior hace backfill
```

---

## 8. Descomposición en HUs

Estimación Fibonacci. Sprint: **siguiente al activo**, no el actual.

| HU | Título sugerido | SP | Depende |
|---|---|---|---|
| **HU-A** `[BACKEND]` Extraer rúbrica Kyverum, persistir path+sha, backfill en GET y en download del FUR | 5 | ADR-0054 Propuesto; spike extractor + fixture |
| **HU-B** `[BACKEND]` Captura en outbox **antes** del auto-FUR, idempotente, sin tumbar el evento `completed` | 3 | HU-A |
| **HU-C** `[BACKEND]` Estampar PNG + leyenda en documentos cuando la vía es identidad | 5 | HU-A; dictamen expert-doc; no cruza #11146 |
| **HU-D** `[QA]` TCs recorte / fallback sello / reuso entre trámites / baúl sigue ganando | 2 | HU-C |
| **HU-E** (opcional, no v1) Regenerar documentos al completar backfill tardío | 3 | Producto |

Front: **NA** en v1.

---

## 9. Lista de archivos (aprox.)

### Crear

| Archivo | HU |
|---|---|
| `services/core-api/docs/adr/ADR-0054-recorte-firma-certificado-kyverum.md` | Antes de HU-A |
| `docs/plan-tecnico-extraccion-firma-identidad-kyverum.md` | este documento |
| `Flit.Tramites.Application/Identity/IIdentitySignatureExtractor.cs` | A |
| `Flit.Tramites.Application/Identity/IIdentitySignatureArtifactStorage.cs` | A |
| `Flit.Tramites.Application/Identity/IIdentitySignatureCapture.cs` + `IdentitySignatureCapture.cs` | A |
| `Flit.Infrastructure/Documents/IdentitySignatureExtractor.cs` | A |
| `Flit.Infrastructure/Storage/IdentitySignatureArtifactStorage.cs` | A |
| `Flit.Infrastructure/Migrations/YYYYMMDDHHMMSS_IdentitySignatureImage.cs` | A |
| `tests/.../IdentitySignatureExtractorTests.cs` + `Fixtures/KyverumCertificate/*.pdf` | A |
| `tests/.../IdentitySignatureCaptureTests.cs` | A/B |
| `tests/.../FurIdentitySignatureStampTests.cs` | C |

### Modificar

| Archivo | HU |
|---|---|
| `ProcedureInstanceBiometricValidation.cs` | A |
| `ProcedureInstanceBiometricValidationConfiguration.cs` | A |
| `Flit.Infrastructure.csproj` / `Directory.Packages.props` (si hay paquete nuevo) | A |
| `InfrastructureExtensions.cs` + `DependencyInjection.cs` | A |
| `DescargarCertificadoIdentidadHandler` (`CertificadoIdentidadCommand.cs`) | A |
| `FurCommand.TryDownloadIdentityCertificateAsync` | A |
| `IdentityValidationOutboxProcessor.cs` | B |
| `IFurDocumentGenerator.cs` (`FurDocumentData`) | C |
| `FurCommand.cs` (ensamblado + exclusividad) | C |
| `FlitFirmaBlock.cs`, `FurFieldMapper.cs`, `MandatoPdfGenerator.cs`, `SolicitudVirtualPdfGenerator.cs` | C |
| `contracts/openapi/core-api.v1.yaml` | Solo si se expone el path (v1: **no** exponer) |

**No tocar:** `IdentityValidationResultApplier` (salvo un comentario de que la captura es post-evento), webhook HTTP, `tramites-client.downloadBiometricCertificado`, `IdentityCertificatePdfGenerator`.

---

## 10. Fases de implementación (orden de merge)

1. **ADR-0054 Propuesto** + dictamen experto en paralelo (HU-C espera dictamen; A/B no).  
2. **HU-A** en rama `feature/AB-{id}-firma-identidad-kyverum`: spike → migración → capture + backfill. DoD: GET certificado deja path; FUR download deja path; tests del fixture.  
3. **HU-B:** outbox paso 0. Test: mock client cuenta **1** download en el ciclo completed.  
4. **HU-C:** estampa. Tests: baúl gana; identidad con path pinta imagen; sin path pinta sello.  
5. **dev-tester** encadenado por HU; evidencias ADO.  
6. **HU-D** QA.  
7. PR ≤ 800 líneas: **un PR por HU** (A ya puede irse al límite por fixture PDF).

---

## 11. Riesgos

| ID | Riesgo | Mitigación |
|---|---|---|
| R1 | Kyverum aplana la página: XObject vacío | Spike HU-A con PDF real de DEV; plan B raster |
| R2 | Layout del certificado cambia | Anclas de texto, no bbox fijo; fallback sello; log `layout_miss` |
| R3 | Watermark BORRADOR en el recorte raster | Umbral de tinta; preferir XObject |
| R4 | Auto-FUR gana la carrera al PDF | D3 + backfill; HU-E si Producto exige imagen en el primer PDF |
| R5 | PII en tests/fixtures | PDF sanitizado; no copiar `context/muestras/` |
| R6 | `SaveAsync` exige Guid de instancia | Reusar el truco del baúl: `tenantId` como agrupador |
| R7 | Mezclar PNG identidad con metadatos baúl | Diccionario `FirmaIdentidadImagenes` (D5) |

---

## 12. Gates antes de codear

1. Feature + HUs en ADO (`feature-creator` / `flit-crear-hu`), tag `DOR`, AssignedTo humano, sprint **siguiente**.  
2. Confirmación **sí** para pasar la HU a Active.  
3. ADR-0054 en **Propuesto** (Líder Técnico acepta).  
4. HU-C: dictamen `expert-doc-engine` sobre el recuadro de firma del FUR.  
5. `sistemas-externos: ADO` solo cuando se registren work items; este plan **no** los crea.

---

## 13. Fuera de alcance v1

- Preview de la rúbrica en UI admin o en el paso identidad.  
- Regeneración automática de FUR al completar backfill.  
- Endpoint Kyverum nuevo (no existe).  
- Recorte de selfie / cédula.  
- Cambiar el consolidado (el PDF del certificado sigue entero).  
- Provider mock.

---

## 14. Checklist de roles al cerrar cada HU

| Rol | Responsable | v1 |
|---|---|---|
| UX/UI | frontend-agent + design-guardian | NA (sin pantalla) |
| Dev backend | backend-agent + database-agent (HU-A) | A/B/C |
| Norma FUR | expert-doc-engine | Antes de C |
| Test unitario | dev-tester | Al cerrar cada HU |
| QA | qa-agent | HU-D |
| Review | code-review-agent | Cada PR |
| Seguridad | inline + PII columnas | HU-A |
