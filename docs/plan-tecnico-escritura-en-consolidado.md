# Plan técnico — Adjuntar la escritura vigente de la compañía al PDF consolidado

> **Origen:** solicitud del usuario (extensión de #10899, consumo de escrituras). · **Fecha:** 2026-07-24 · **Estado:** Propuesto (pendiente aprobación humana)
> **Feature padre:** #10899 (Representantes legales y escrituras por compañía) · **ADR:** 0033
> **Alcance:** 1 HU BACKEND. No inicia código hasta descomposición aprobada y gates FLIT.

---

## 1. Objetivo y alcance

Al generar el expediente de un trámite, si un actor (comprador y/o vendedor) es una **compañía (NIT)** con una **escritura activa y vigente** registrada en el directorio del tenant (#10899, `admin.company_deeds`), incluir ese PDF como **adjunto del sistema** para que entre automáticamente al **PDF consolidado** del expediente, de forma **idempotente** y en una **posición determinística**.

**Fuera de alcance:** cambios en los ensambladores del consolidado (no se tocan), firmas sobre la escritura, branding por gestora.

### Decisiones confirmadas por el usuario (2026-07-24)

| # | Decisión | Elección |
|---|----------|----------|
| **D1** | ¿Escritura de qué actor(es)? | **Vendedor y comprador** — la escritura vigente de cada actor-compañía (PJ/NIT) que la tenga. |
| **D3** | ¿En qué estados se adjunta? | **Siempre** (cualquier estado, incl. borrador) — es documentación de soporte, no una firma. |
| **D2** | Varias vigentes por compañía / dos compañías | Por NIT: la de **mayor vigencia** (colapso, como el collapse existente). Dos roles PJ ⇒ **un tipo de adjunto por rol** (`escritura` para vendedor/propietario, `escritura_comprador` para comprador) para no colisionar con el dedup del FUR. |
| **D4** | Posición en el consolidado | Traspaso: tras `compraventa`. Matrícula: tras `certificado_rues`. |
| **D5** | Tipo de documento (catálogo) | Nuevo `document_types.code='escritura'` (coherente con el tag de storage de `DeedDocumentStorage`). |
| **D6** | Obtención de bytes | Puerto nuevo `IProcedureDeedResolver` (Trámites.Application) implementado en Infrastructure; **no** se exponen bytes en `IDeedDocumentStorage`. |

---

## 2. Hallazgo clave (por qué es de bajo riesgo)

El consolidado **mergea cualquier `ProcedureInstanceAttachment` que sea PDF** y esté en el orden. Por lo tanto **no hay que tocar los ensambladores**: basta **inyectar la escritura como un adjunto `Source="system"`** (igual que el certificado RUES) y fluye sola al consolidado (wizard **y** maestro).

Ambos ensambladores mergean desde `Attachments`:
- `GenerarConsolidadoHandler` — `ConsolidadoCommand.cs:56-78` (`ConsolidadoOrderingResolver.Select` → `storage.OpenReadAsync` → `merger.NormalizeToPdf` → `merger.Merge`).
- `GenerarConsolidadoMaestroHandler` — `ConsolidadoMaestroCommand.cs:71-97` (#10701, botón único, cachea con flag `ConsolidadoMaestroVigente`).
- Merge: `PdfExpedienteConsolidadoMerger.cs:34` (PdfSharpCore). `IsMergeableMime` acepta `application/pdf` — la escritura pasa.

---

## 3. Diseño (costuras verificadas)

### 3.1 Punto de enganche — `GenerarFurHandler` (patrón RUES)
`FurCommand.cs:48` (`HandleAsync`). Tras generar RUES (`:147`) y RNMC (`:168`):
1. Resolver, para cada actor con `DocumentType=="NIT"` (`ProcedureInstanceActor.cs:11`, roles comprador/vendedor), su escritura vigente **con bytes**.
2. Agregar a la lista `generated` (`FurCommand.cs:107`; record `GeneratedDocument(Tipo, Filename, Mimetype, byte[] Content)` en `IFurDocumentGenerator.cs:95`):
   - `GeneratedDocument("escritura", "escritura.pdf", "application/pdf", bytes)` para el vendedor/propietario.
   - `GeneratedDocument("escritura_comprador", "escritura-comprador.pdf", "application/pdf", bytes)` para el comprador PJ (si aplica).
3. El **bucle idempotente existente** (`FurCommand.cs:186-216`) persiste cada `generated`: borra el previo del mismo tipo (`:189-195`), `storage.SaveAsync` (`:197`), crea `ProcedureInstanceAttachment` con `Source="system"` (`:209`). **Cero cambios en los ensambladores.**

> Reutiliza la detección de actor jurídico ya usada por el FUR (`EsActorJuridico`, `FurCommand.cs:338`) y el patrón `instance.Actors.FirstOrDefault(a => a.DocumentType == "NIT")` (`:445`).

### 3.2 Puerto de resolución de escrituras (D6)
**Crear** `IProcedureDeedResolver` (Trámites.Application):
```csharp
Task<IReadOnlyList<ResolvedDeed>> ResolveForActorsAsync(
    Guid tenantId, IReadOnlyList<ActorRef> actors, DateOnly today, CancellationToken ct);
// ResolvedDeed(string Rol, string Nit, byte[] Content)  // o Stream
```
Implementación en **Infrastructure** (puede cruzar módulos), reusando lo existente:
- `ILegalRepresentativeReader.FindRepresentedCompanyByNitAsync(tenant, nit)` (`ILegalRepresentativeReader.cs:53`) → id de compañía.
- `IDeedReader.ListActiveVigentesAsync(tenant, today)` (`DbDeedReader.cs:78`, filtra `IsActive && VigenciaDesde<=today && VigenciaHasta>=today`) → `DeedItem` cuya `RepresentedCompanyIds` contenga el id; colapso por NIT a la de **mayor vigencia**.
- `DeedItem.StoragePath` (`LegalRepresentativeReadModels.cs:71`) + `IAttachmentStorage.OpenReadAsync(storagePath)` (`IAttachmentStorage.cs:62`) → bytes.

> Nota: `IDeedDocumentStorage` hoy solo expone `CreateUploadAsync` + `GetViewUrlAsync` (`IDeedDocumentStorage.cs:35,43`), **no** descarga de bytes. Por eso el resolver lee el `storage_path` de `admin.company_deeds` y baja los bytes con `IAttachmentStorage.OpenReadAsync` desde Infra, sin romper el aislamiento del puerto Admin.

### 3.3 Orden + catálogo (D4, D5)
- **Modificar** `TraspasoConsolidadoOrdering.cs:13` (array `Precedence`): insertar `"escritura"` y `"escritura_comprador"` tras `"compraventa"`.
- **Modificar** `MatriculaConsolidadoOrdering.cs:11`: insertar `"escritura"` tras `"certificado_rues"`.
- *(Genérico: opcional; los no listados caen al final como "Anexos", `GenericConsolidadoOrdering.cs:58-73`.)*
- **Crear** migración idempotente: seed `tramites.document_types` (`code='escritura'`, nombre "Escritura", mimes pdf) — hoy solo existe `escritura_publica` (`23-HU10520-document-types-seed.sql:46`). No editar la migración vieja.

### 3.4 Regeneración e invalidación (R1)
El adjunto se inyecta al generar el FUR (idempotente). El consolidado del wizard regenera siempre. El **consolidado maestro** cachea; para que una escritura recién añadida se refleje, poner `ProcedureInstance.ConsolidadoMaestroVigente=false` al inyectar (mismo mecanismo que `LicenciaTransitoCommand.cs:86`).

---

## 4. Criterios de aceptación (Gherkin)

- **AC1 — Adjunto automático (vendedor y comprador):** *Dado* un traspaso cuyo vendedor y/o comprador es PJ con NIT que tiene escritura activa y vigente en el directorio del tenant, *cuando* se genera el FUR/expediente, *entonces* la(s) escritura(s) quedan como `ProcedureInstanceAttachment` (`escritura` / `escritura_comprador`), `Source="system"`, y **aparecen en el PDF consolidado** en su posición (tras compraventa / tras certificado_rues).
- **AC2 — Cualquier estado:** *Dado* un trámite en borrador, *entonces* la escritura se adjunta igual (D3).
- **AC3 — Idempotencia:** *Dado* que se regenera el expediente, *entonces* se reemplaza la escritura previa del mismo tipo sin duplicar.
- **AC4 — Sin escritura / no vigente:** *Dado* un actor sin escritura o con escritura vencida/inactiva, *entonces* no se adjunta nada y el consolidado se genera sin error.
- **AC5 — Aislamiento por tenant:** *Dado* un NIT con escritura en OTRO tenant, *entonces* no se adjunta (cruce solo dentro del tenant, RLS).
- **AC6 — Colapso por vigencia:** *Dado* una compañía con varias escrituras vigentes, *entonces* se adjunta la de mayor vigencia.

---

## 5. Archivos a crear/modificar (mapa file:line)

- **Crear** `Flit.Tramites.Application/Documents/IProcedureDeedResolver.cs` + records (`ResolvedDeed`, `ActorRef`).
- **Crear** `Flit.Infrastructure/Documents/ProcedureDeedResolver.cs` (reusa `IDeedReader`, `ILegalRepresentativeReader`, `IAttachmentStorage`); registrar en DI (`InfrastructureExtensions`).
- **Modificar** `FurCommand.cs` — inyectar el resolver (ctor `:33`); bloque de escrituras tras RNMC (`:168`); `generated.Add(...)`; `ConsolidadoMaestroVigente=false`.
- **Modificar** `TraspasoConsolidadoOrdering.cs:13`, `MatriculaConsolidadoOrdering.cs:11` (arrays `Precedence`).
- **Crear** migración idempotente (seed `document_types.escritura`).
- **Tests** (`dev-tester`): unit del resolver (cruce NIT→escritura vigente, colapso, aislamiento tenant) + del hook FUR (adjunto creado / idempotente / no-vigente / dos roles PJ).

---

## 6. Riesgos

- **R1 — Caché del consolidado maestro (#10701):** una escritura añadida mid-flujo podría no invalidarlo. *Mitigación:* `ConsolidadoMaestroVigente=false` al inyectar.
- **R2 — Re-subida de bytes en cada regen de FUR** (igual que otros generados). Aceptable (límite 20 MB, `AttachmentsCommand.cs:37`).
- **R3 — Paridad catálogo↔runtime:** `escritura`/`escritura_comprador` deben estar en los `Precedence` o quedan como "Anexo" al final.
- **R4 — Dedup por tipo:** dos roles PJ exigen tipos distintos (`escritura` vs `escritura_comprador`); con un solo tipo el bucle del FUR conservaría solo uno.

---

## 7. Gate

Propuesto. Requiere: aprobación humana de D1-D6 y R1-R4; registro de la HU (hecho: New/Sprint siguiente/DOR sin activar); activación con gate humano antes de implementar; PR ≤800 líneas a `develop`; Code Review + Security; reviewer humano. Ver [[plan-rl-escrituras-por-compania]], [[plan-mandato-solicitud-virtual]].
