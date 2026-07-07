# Plan de implementación — Feature #10585 · Gestión de Prenda (F2)

> **Fecha:** 2026-07-07 · **Autor:** Claude Code · **Base:** `docs/descomposicion-hus-segunda-ola.md` §5 + reconocimiento del código (`services/core-api`).
> **Requerimientos:** IT-3, R4, R10, R17 + marcación FUR · **Esfuerzo:** ~40 SP (8 HUs: 5 BE + 3 FE).
> **Estado:** listo para arrancar. **Sin dependencias externas ni bloqueantes** (ver §1).

---

## 1. Dependencias y bloqueantes — veredicto

**F2 es AUTÓNOMO. No tiene bloqueantes. Se puede arrancar YA, incluso en paralelo con otros features.**

| Chequeo | Resultado |
|---------|-----------|
| ¿Depende de F1 (#10535)? | **No.** F2 trae su propio cimiento (IT-3). Confirmado en la matriz §1 de la descomposición. |
| ¿Depende de otro feature de la 2ª ola? | **No.** F10/F11/F4/F3 son ortogonales. |
| ¿Requiere módulo de terceros inexistente? | **No** (a diferencia de F5). |
| ¿Requiere ADR de seguridad? | **No** (a diferencia de F12). |
| ¿Requiere nueva migración de BD? | **Sí**, pero es aditiva (tabla nueva `tramites.procedure_instance_prenda`, DDL `24`, sin tocar tablas existentes). |
| ¿La señal de gravámenes ya existe? | **Sí.** El RUNT ya reporta `prendas`/`gravamenes` como check `warn` (ver §3). No hay que integrar nada nuevo. |

**Riesgo interno principal (no bloqueante):** el **versionado post-registro (R17)**. No existe infraestructura de versionado de datos en el repo (los `field_values` son inmutables tras `draft` por trigger de BD). **La solución ya está en el diseño**: la prenda vive en su **propia tabla con `estado vigente/reemplazada`**, así el versionado es intrínseco al agregado y **no** toca la barrera de inmutabilidad de `field_values`. Esto es lo que hace viable R17 sin refactor peligroso.

**Decisión de arquitectura fundacional (la más importante del feature):**
> La prenda **NO es una tipología/modalidad nueva** ni un `procedure_type` nuevo. Es un **agregado compañero** (`ProcedureInstancePrenda`) adosado a cualquier `ProcedureInstance` (matrícula o traspaso), con su propia tabla, su propio ciclo de vida (`vigente`/`reemplazada`) y su propia auditoría.
>
> **Consecuencia positiva:** NO se tocan `TramiteTipologiaCatalog`, `TipologiaMatrizCatalog`, `TramiteModalidadEntrada` ni los tests de drift (`DriftIssues`, aserción hardcodeada `esperados 6/5`). Se evita todo el riesgo de sincronizar los dos catálogos.

---

## 2. Modelo de dominio propuesto (IT-3)

Nueva tabla `tramites.procedure_instance_prenda` (RLS por tenant, patrón de `23-HU10545-ot-requirements.sql`):

| Columna | Tipo | Nota |
|---------|------|------|
| `id` | uuid PK | |
| `procedure_instance_id` | uuid FK | → `procedure_instances` |
| `tenant_id` | uuid | RLS `USING (tenant_id = current_setting(...))` |
| `decision` | varchar | `solicitar` \| `registrar` \| `levantar` \| `omitir` \| `sin_prenda` |
| `estado` | varchar | `vigente` \| `reemplazada` (**el versionado**) |
| `acreedor_nombre` | varchar null | beneficiario del gravamen (para el FUR) |
| `acreedor_documento` | varchar null | NIT/CC del acreedor |
| `metadata` | jsonb | monto, fecha, notas |
| `created_by` / `created_at` | | auditoría |
| `row_version` | | `trg_row_version` (concurrencia optimista) |

- **Invariante:** a lo sumo **una fila `vigente`** por `procedure_instance_id`. Índice único parcial `WHERE estado='vigente'`.
- **Versionado (R17):** modificar = insertar nueva fila `vigente` + `UPDATE` de la anterior a `reemplazada`, en la misma transacción. Historial completo por diseño.
- **Auditoría:** trigger `trg_audit_log` sobre la tabla (→ `audit.audit_logs`) + un `ProcedureInstanceEvent` `Tipo="prenda_modificada"` con `Payload` = decisión anterior/nueva (patrón de `fur_generado`).

**Value objects nuevos** (`Flit.Tramites.Domain/Tramites/ValueObjects/`):
- `PrendaDecision` — constantes + set `RequierenDocumento = { solicitar, registrar, levantar }` (omitir/sin_prenda no exigen doc).
- `PrendaEstado` — `Vigente`/`Reemplazada`.

**Agregada** `Flit.Tramites.Domain/Entities/ProcedureInstancePrenda.cs` (espejo de `ProcedureInstanceActor` en estructura).

**DocTipos nuevos** en `AttachmentRules.ValidTipos`: `prenda_solicitud`, `prenda_registro`, `prenda_levantamiento`.

---

## 3. Anclas de reutilización (lo que YA existe)

| Necesidad | Reutiliza | Ubicación |
|-----------|-----------|-----------|
| Señal de gravamen/prenda del vehículo | Check `gravamenes` (`warn`) que ya emiten los mappers de RUNT | `VerifikResultMapper.cs:94-105`, `KyverumRuntVehicleResultMapper.cs:86-96`, `IntempoVehicleResultMapper.cs:78-93`; parseo en `IntempoVehicleResponse.cs:51-154` (`prendas`/`nombreAcreedor`/`estadoPrenda`) |
| Marcar la prenda en el FUR | Checkbox `requested_process_11` **ya existe** (hoy forzado a `false`) | `FurFieldMapper.cs:157-163` (`MarkTramite`) + manifiesto `fur-field-manifest.json:19` |
| Puente config→gate (si se necesitara flag por OT) | Patrón `IIdentityValidationPolicy`/`IdentityValidationPolicy` | `TramiteLifecycleService.cs:80` + `Flit.Infrastructure/OtRules/` |
| Gate de traspaso | Rama nueva en `SubmitGate.EvaluateTraspaso` + código en `TramiteEstadoErrores` | `SubmitGate.cs:71`, `TramiteEstadoErrores.cs` |
| Gate a nivel de paso wizard | `TraspasoGates.PasoCompleto` + `PasoDataKeys` | `TraspasoGates.cs:12,17` |
| Persistencia de decisión (campos sueltos) | `PatchFieldValuesHandler` (upsert por `FieldKey`) — **solo para señales, no para la prenda misma** | `PatchFieldValuesCommand.cs:16` |
| Migración | DDL embebido `24-HU10585-prenda.sql` + `EmbeddedDdl.LoadUp` en migración EF | patrón `23-HU10545-ot-requirements.sql` |
| Certificado/documento generado | QuestPDF (`RuesCertificatePdfGenerator`) si hiciera falta un doc de prenda | `Flit.Infrastructure/Documents/` |

**Nota sobre el gate de traspaso (R10):** el disparador es el semáforo de gravámenes en `warn` (ya existe como check del RUNT). **No se requiere flag por OT** — el gate es global, dirigido por la señal. (Se deja anotado que, si el negocio lo pidiera después, se añadiría `pledge_gate_enabled` a `admin.ot_requirements` con el patrón de `IdentityValidationEnabled`; **fuera de alcance ahora**.)

---

## 4. Desglose por HU (orden de ejecución)

Rama única del feature `feature/AB-10585-prenda` desde `develop`, **1 commit por HU**. BE precede a FE. HU-F2-08 (FUR) puede ir en paralelo tras HU-F2-01.

### HU-F2-01 · [BE] Cimiento IT-3 — entidad, tabla, comando base · **8 SP**
- `ProcedureInstancePrenda.cs` (agregada) + `PrendaDecision`/`PrendaEstado` (VOs).
- DDL `24-HU10585-prenda.sql` (tabla + RLS + índice único parcial `vigente` + `trg_row_version` + `trg_audit_log`) + migración EF `.Sql` idempotente. Config EF (`IEntityTypeConfiguration`).
- `PrendaCommand.cs`: `RegistrarPrendaHandler` (crea `vigente`, reemplaza anterior) + `GetPrendaVigenteHandler`.
- `IProcedureInstancePrendaRepository` + impl en `Flit.Infrastructure`.
- DocTipos en `AttachmentRules.ValidTipos`.
- **Tests:** dominio (invariante 0..1 vigente, transición vigente→reemplazada), repo, comando.

### HU-F2-02 · [BE] R4 — Prenda declarativa en matrícula · **5 SP**
- Endpoint `PUT /instances/{id}/prenda` (`PrendaEndpoints.cs`) con decisiones `registrar`/`sin_prenda`.
- **Informativa: NO bloquea la radicación en matrícula** (no toca gates de matrícula).
- **Tests:** guardar decisión no bloquea; `sin_prenda` no exige documento.

### HU-F2-03 · [FE] R4 — Formulario de prenda en matrícula · **3 SP**
- `PrendaForm.tsx` en el paso comprador/documentos de matrícula; tipos + cliente API.
- **Tests (Vitest):** "sin prenda" no exige documento y permite continuar.

### HU-F2-04 · [BE] R10 — Prenda como gate en traspaso · **8 SP**
- En `SubmitGate.EvaluateTraspaso`: si el check `gravamenes` está en `warn` y **no hay decisión de prenda vigente**, bloquear con `prenda_decision_requerida`. Si la decisión requiere documento (`RequierenDocumento`) y falta el adjunto → `prenda_documento_requerido`.
- Códigos nuevos en `TramiteEstadoErrores.cs`. Enforce a nivel de paso en `TraspasoGates.PasoCompleto` (paso comercial).
- **Tests:** warn sin decisión bloquea; decisión que requiere doc sin adjunto bloquea; interacción con "asumo el riesgo".

### HU-F2-05 · [FE] R10 — Prenda en el paso comercial del traspaso · **5 SP**
- `PrendaForm.tsx` + `CommercialForm.tsx` con las 4 decisiones y carga de documentos; render de estados del gate (copy en `wizard-copy.ts`).
- **Tests (Vitest):** el paso no avanza sin decisión; mensajes del gate.

### HU-F2-06 · [BE] R17 — Modificar prenda post-registro (versionado) · **5 SP**
- `PUT /prenda` habilitado **fuera de `borrador`** solo para esta acción (excepción acotada, NO se relaja `trg_field_value_immutable` porque la prenda vive en su propia tabla).
- Nueva fila `vigente` marca la anterior `reemplazada`; `ProcedureInstanceEvent` `prenda_modificada` con old/new; bloquear en estados finales (`TramiteEstado.EsFinal`).
- **Tests:** modificar tras registro crea fila vigente + reemplaza + audita; bloqueo en estado final.

### HU-F2-07 · [FE] R17 — Modificar prenda desde el detalle · **3 SP**
- Acción "Modificar elección de prenda" en el detalle del trámite (post-registro); refresco del estado vigente.
- **Tests (Vitest):** el detalle refleja la nueva prenda vigente.

### HU-F2-08 · [BE] Marcación de la prenda en el FUR · **3 SP**
- Propagar un flag `HasPrenda`/datos del acreedor por `FurDocumentData` (`IFurDocumentGenerator.cs:46`), poblado en `FurCommand.AssembleData` leyendo la prenda `vigente`.
- En `FurFieldMapper.MarkTramite` (`FurFieldMapper.cs:157-163`): poner `requested_process_11 = true` cuando hay prenda `registrar` vigente; `sin_prenda`/`omitir` → sin gravámenes. Si se requieren campos de acreedor en el PDF, añadirlos a `fur-field-manifest.json` + emitir tokens.
- **Puede ir en paralelo** tras HU-F2-01 (solo necesita leer la prenda vigente).
- **Tests:** prenda `registrar` marca el gravamen; `sin_prenda` no lo marca.

---

## 5. Archivos que se tocan (mapa rápido)

**Nuevos (BE):** `Domain/Entities/ProcedureInstancePrenda.cs`, `Domain/Tramites/ValueObjects/PrendaDecision.cs` + `PrendaEstado.cs`, `Domain/Tramites/{IProcedureInstancePrendaRepository}.cs`, `Application/UseCases/ProcedureInstances/PrendaCommand.cs`, `Api/Endpoints/Tramites/PrendaEndpoints.cs`, `Infrastructure/Persistence/Repositories/ProcedureInstancePrendaRepository.cs` + config EF, `Infrastructure/Persistence/Sql/Ddl/24-HU10585-prenda.sql`, `Infrastructure/Migrations/<ts>_HU10585_Prenda.cs`.
**Modificados (BE):** `SubmitGate.cs`, `TramiteEstadoErrores.cs`, `TraspasoGates.cs`, `AttachmentsCommand.cs` (DocTipos), `FurCommand.cs` + `IFurDocumentGenerator.cs` + `FurFieldMapper.cs` (+ manifiesto si aplica), DI en `InfrastructureExtensions.cs`.
**Nuevos/mod. (FE):** `components/operacion/PrendaForm.tsx`, `CommercialForm.tsx`, `wizard-copy.ts`, tipos + cliente API, acción en el detalle del trámite.

---

## 6. Verificación y gates

- **Sin migración destructiva:** DDL `24` aditivo (tabla nueva + triggers). Generar con Infrastructure como startup.
- **Build/tests:** `Flit.Tramites.Domain(.Tests)`, `Application(.Tests)`, `Infrastructure`, `Flit.Api`; Vitest de los componentes nuevos. `dotnet format --include <archivos>` acotado (lección de encoding: evita churn CRLF).
- **Gates FLIT:** activar cada HU (`New→Active`) con confirmación humana explícita; PR a `develop` ≤ 800 líneas (si el feature completo excede, dividir el PR por HU o por bloque R4/R10/R17/FUR); merge con reviewer humano real + Code Review/Security/Build.

---

## 7. Recomendación de arranque

Orden sugerido dentro del feature: **F2-01 → (F2-02→F2-03) → (F2-04→F2-05) → (F2-06→F2-07) → F2-08**.
F2-08 (FUR) se puede intercalar en paralelo tras F2-01. Dado el peso (~40 SP / 8 HUs), **considerar 2 PRs**: PR-A = R4 + IT-3 + FUR (fundacional + valor rápido), PR-B = R10 + R17 (gate + versionado, la parte de más riesgo), para respetar el límite de 800 líneas y facilitar el review.
