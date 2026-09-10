# Plan técnico — Mandato + Solicitud de trámite virtual

- **Origen:** `mandato-req.txt` (raíz del repo) + plantillas Handlebars legacy de FLIT 1.0 (`D:\FLIT\BackCrudTransfer_master\src\assets\templates\pdf\mandated\*.hbs` y `virtual-process\*.hbs`).
- **Base de código analizada:** rama local **`feature/AB-10899-representantes-escrituras`** (8 commits, HU #10900–#10907 sobre `e8a2af7a`). Todo lo que se cita abajo fue verificado en esa rama, no en `develop` ni en la rama activa.
- **Estado:** Propuesto — 2026-07-23. Sin código escrito.
- **Regla FLIT:** ningún trabajo arranca sin Feature/HUs en ADO y activación humana (gate §11).

---

## 1. Objetivo y alcance

Añadir al expediente **dos documentos autogenerados por el sistema** (PDF, `Source=system`), que se persisten como adjuntos del trámite y entran al consolidado:

| Documento | Tipo | ¿Cuándo aplica? | Variantes |
|---|---|---|---|
| **Mandato** (contrato privado de mandato) | `mandato` | Radicador **persona jurídica (NIT)**; además **persona natural** en los OT que lo exijan (hoy: **Sabaneta**) | Por **OT** (Sabaneta / Bello / genérico) × **modalidad** (matrícula / traspaso) × **PN/PJ** → 8 plantillas legacy |
| **Solicitud de trámite de forma virtual** | `tramite_virtual` | **Siempre**, en todo trámite | Solo cambia **quién firma** (PN a nombre propio; PJ el representante legal) → 2 plantillas legacy |

Fuera de alcance: portar el motor Handlebars/HTML→PDF (se reimplementa en QuestPDF, §3 D7), branding/membrete (vive en el Feature #10852) y el mandato *manual* que el cliente sube (`poder_tramitador`, ya existe en el catálogo).

---

## 2. Glosario: quién es quién (crítico — el requerimiento mezcla los términos)

| Figura | Quién es | Dónde vive HOY en FLIT 2.0 |
|---|---|---|
| **MANDANTE** | Quien radica: el propietario/comprador/vendedor. Si es **PJ**, firma su **representante legal**; si es **PN**, firma él mismo. | `tramites.procedure_instance_actors` + `IdentitySubjectResolver` (para PJ, el RL embebido en `actor.metadata.representanteLegal`) — `services/core-api/src/Flit.Tramites.Application/UseCases/ProcedureInstances/IdentitySubjectResolver.cs`. Precargable desde el directorio RL de HU #10900. |
| **MANDATARIO** | El representante legal que **el OT registra** para una **compañía gestora**. Es quien recibe el poder. | `admin.mandate_signers` + `admin.mandate_signer_companies` (ADR-0023), llave `(transit_office_id, company_tenant_id)`. Admin en `/admin/transit-offices/[id]/mandatarios`. |

> El requerimiento dice *"Pueden existir varios **Mandantes** por OT-compañía"*, pero el punto 14 lo aclara: *"el OT tiene registrados varios **mandatarios** para esa compañía"*. **Se lee como mandatarios.** Confirmar en §10 R1.

En Sabaneta el mandatario es **institucional y fijo** (UT-SETSA, NIT 900273813-7) y **solo firma el mandante**; en Bello es una **persona** que además se identifica como representante legal de la UT-MAB (NIT 901.783.814-6); en el genérico es una **persona** sin compañía.

---

## 3. Costuras validadas en `feature/AB-10899-representantes-escrituras`

| Necesidad del requerimiento | Costura existente | Estado |
|---|---|---|
| Mandatario por OT + compañía | `admin.mandate_signers` / `mandate_signer_companies`; `IMandateSignerReader.ListActiveCompanyResolutionsAsync` (lectura cross-tenant con `row_security = off`) | **Existe** — sin `email`, sin identidad y con **exclusividad de 1 activo** |
| Enviar correo de validación de identidad al registrarse | `admin.admin_identity_validations` + `IAdminIdentityValidationService.SendAsync` (HU #10907, ADR-0034). El bloque es **agnóstico del sujeto** (`subject_type` + `subject_ref`, sin FK) y su propio XML-doc dice *"hoy el representante legal; **mañana el mandatario**"* | **Existe** — solo falta un `subject_type` |
| Reenviar el correo (si nunca se hizo o si venció) | `IAdminIdentityValidationService.ResendAsync`: reenvía si no hay una **aprobada+vigente**; si la hay, la reutiliza (`Reused=true`). Sin la guarda `biometria_activa` del flujo de trámite | **Existe** — semántica idéntica a la pedida |
| Vigencia de la validación | `valid_until` (30 días desde la aprobación), `ApproveAsync` / `ReconcileAsync` | **Existe** |
| Firma del mandante (PJ → su RL) | `IdentitySubjectResolver`; sellos de identidad en `FurCommand.cs` (`SellosIdentidad`); imagen real del baúl vía `ISignatureVaultPolicy.ResolveAsync(tenantId, nit)` | **Existe** |
| Precedencia firma baúl > identidad | `LegalRepresentativeSignatureResolver` (`Flit.Admin.Application/Companies/LegalRepresentatives/`) | **Existe** — reutilizable tal cual |
| Generar + adjuntar + idempotencia + limpieza en regeneración | `GenerarFurHandler` (`Flit.Tramites.Application/UseCases/ProcedureInstances/FurCommand.cs`): patrón ya usado por FUR, compraventa, `certificado_rues` y `certificado_rnmc` | **Existe** |
| *"Se agrega automáticamente al consolidado"* | `GenerarConsolidadoMaestroHandler` fusiona **todos** los adjuntos menos los consolidados previos | **Sale gratis** al persistir el adjunto |
| Tipos `mandato` y `tramite_virtual` | Sembrados en `Sql/Ddl/23-HU10520-document-types-seed.sql` | **Existe** |
| Configurador de documentos y orden | `tramites.procedure_document_requirements` (matrices `24-`/`25-HU10522`), `admin.ot_document_precedence`, UI `/admin/documents` y `/admin/transit-offices/[id]/documents` | **Existe** — los 2 tipos **no están** en las matrices |
| Firmas solo en estado ≠ borrador | `TramiteEstado` (`borrador/preparado/entregado/aprobado/…`) + precedente **ADR-0028** (firma automática de compraventa) | **Existe** |
| Regla documental por PN/PJ | `ConditionalDocumentRules` + `TramiteDocumentContext` (`EsNit`, `EsPersonaNatural`, …) | **Hueco**: el contexto **no conoce el OT** |
| Elegir firmante al aprobar | `POST /instances/{id}/transition` → `TransitionProcedureInstanceHandler` → `ITramiteLifecycleService` | **Hueco**: la orden no acepta payload de firmante |

---

## 4. Decisiones de diseño

> **Confirmadas por el usuario/PO (2026-07-23):** D2 (varios mandatarios), R1, R2, R3, R4 y el modelo de mandatarios del punto 3 (§4.3). Las decisiones abajo quedan **fijadas**.

### 4.1 Recomendadas (default si no hay objeción)

- **D1 — Mandatario = ampliar `admin.mandate_signers`, no crear entidad nueva.** Se le añaden `document_type`, `email`, `signature_vault_id` (FK al baúl, `ON DELETE SET NULL`), `identity_validation_ref` (uuid, sin FK) y **`user_id` (FK `identity.users`, `ON DELETE SET NULL`)** — la cuenta de usuario de OT con la que se autentica el mandatario (D9). Es esencialmente la forma de `admin.company_legal_representatives` (HU #10900), así que el resolutor de firma se reutiliza sin adaptador.
- **D2 — Varios mandatarios activos por `(OT, compañía)` [CONFIRMADA].** Se elimina el índice único parcial `uq_mandate_signer_companies_active` (`transit_office_id, company_tenant_id` WHERE `is_active`) y se sustituye por `(transit_office_id, company_tenant_id, mandate_signer_id)` WHERE `is_active`. **Esto deroga la decisión central de `ADR-0023-firmante-mandato-exclusividad-modelo.md` ⇒ exige un ADR nuevo que lo supersede** (regla FLIT 15).
- **D3 — Identidad del mandatario = reusar el bloque HU #10907 con `subject_type = 'mandate_signer'`.** **Cero cambios de esquema** en `admin_identity_validations`. Envío automático al registrar el mandatario (si trae correo) + acción explícita de reenvío. Vigencia 30 días, igual que el RL.
- **D4 — Configuración por OT en tabla propia `admin.transit_office_mandate_config`**, llaveada por `transit_office_id` (catálogo RUNT, `ON DELETE RESTRICT`), dispersa, **sin `tenant_id`**: la regla pertenece al OT y la consumen todas las compañías gestoras. Guarda: código de plantilla, si exige mandato a persona natural, y los datos del mandatario institucional (nombre + NIT) para no hardcodear "UT-SETSA"/"UT-MAB" en el binario.
  - *Descartado:* `admin.ot_feature_flags` — está llaveada por el **tenant del OT**, no por el OT del catálogo, y el consumidor es el tenant de la **compañía gestora**; obligaría a un salto por `transit_office_profiles` en cada generación.
- **D5 — El firmante elegido se persiste en el trámite:** nueva columna `tramites.procedure_instances.mandate_signer_id uuid` (sin FK: cruza a `admin`, mismo patrón que `identity_validation_ref` en HU #10900) + evento `mandato_firmante_seleccionado` en la bitácora.
- **D6 — Firma del mandatario: precedencia baúl > identidad**, la misma regla de `LegalRepresentativeSignatureResolver`. Si **ninguna** está vigente → no se estampa imagen, se pinta el sello de texto y se devuelve una **advertencia visible** al usuario ("el mandatario no tiene firma ni validación vigente"). **No bloquea** la generación (coherente con HU #10463: la identidad ya no bloquea generar).
- **D7 — Los PDF se reimplementan en QuestPDF**, no se porta Handlebars. Precedente directo: `FurCompraventaDocumentGenerator`, `RuesCertificatePdfGenerator`, `RnmcCertificatePdfGenerator`. El texto legal se transcribe **literal** desde los `.hbs`; lo único parametrizado son los datos.
- **D8 — Un solo generador por documento, con un `MandateTemplateResolver` puro en `Flit.Tramites.Domain`** que devuelve la variante desde `(código OT, tipología, PN/PJ)`. Sin `if` de OT esparcidos por Infrastructure.
- **D9 — Auto-resolución del firmante por `user_id`, no por documento [CONFIRMADA].** El usuario de OT **no captura documento** (`identity.users` = email + display_name; invitación = email + rol). Por eso el mandatario se **vincula a su cuenta de usuario** (`mandate_signers.user_id`) al registrarlo, y el match al firmar es `mandatario.UserId == usuario autenticado (sub)`. El documento del mandatario (`document_number`) se mantiene solo como dato del PDF, no como llave de cotejo.

### 4.2 Resueltas (2026-07-23)

- **R1 [SÍ]** — "varios Mandantes por OT-compañía" = varios **mandatarios**.
- **R2 [confirmada]** — El mandato es un documento **autogenerado** por el sistema (mismo handler/idempotencia que RUES/RNMC), se adjunta y entra al consolidado, y se **siembra en las matrices documentales** para colocarlo en el orden correcto (no "Anexos"). No es carga manual.
- **R3 [SÍ, propuesta aceptada]** — El mandato se **genera desde `preparado`** (con firmante auto-resuelto o filtrado si aplica; sin firma del mandatario si no se pudo resolver) y se **regenera al aprobar** con el firmante elegido/filtrado.
- **R4 [SÍ]** — **Sabaneta y Bello son los únicos OT con plantilla propia**; el resto usa la genérica. No se implementa carga de plantillas por consola en esta entrega (queda como mejora futura).

### 4.3 Modelo del mandatario (punto 3 — validado con el PO)

Reglas fijadas para la firma del mandato:

1. **Al crear un mandatario se selecciona a qué compañías aplica** (multiselect ya existente en `MandatarioFormPanel`).
2. **Pueden existir varios mandatarios** (D2) y **un mandatario puede aplicar a varias compañías** (la relación `mandate_signer_companies` ya es M:N).
3. **Auto-resolución del firmante** cuando una compañía tiene **varios** mandatarios activos: se filtra por **`user_id` = usuario autenticado** que aprueba el trámite (D9); si hay **match** se **setea automáticamente** ese mandatario. Si **no hay match** → se permite **seleccionar el mandatario al aprobar**.
   - El mandatario **es** el usuario que se autentica; por eso se vincula su `user_id` al registrarlo. El documento del usuario **no se captura** en FLIT 2.0, así que el cotejo es por identidad de cuenta, no por cédula.

---

## 5. Modelo de datos (2 migraciones nuevas)

### 5.1 `41-HU109xx-mandate-signers-identity.sql`

```sql
-- Amplía el mandatario (ADR-0023) con correo, tipo de documento y vínculo firma/identidad.
ALTER TABLE admin.mandate_signers
  ADD COLUMN IF NOT EXISTS document_type varchar(10) NOT NULL DEFAULT 'CC',
  ADD COLUMN IF NOT EXISTS email varchar(200),
  ADD COLUMN IF NOT EXISTS signature_vault_id uuid
      REFERENCES admin.signature_vault(id) ON DELETE SET NULL ON UPDATE CASCADE,
  ADD COLUMN IF NOT EXISTS identity_validation_ref uuid,
  -- D9 — cuenta de usuario de OT del mandatario (llave del cotejo al firmar).
  ADD COLUMN IF NOT EXISTS user_id uuid
      REFERENCES identity.users(id) ON DELETE SET NULL ON UPDATE CASCADE;

COMMENT ON COLUMN admin.mandate_signers.email IS '@pii';
CREATE INDEX IF NOT EXISTS ix_mandate_signers_signature_vault_id
  ON admin.mandate_signers(signature_vault_id);
CREATE INDEX IF NOT EXISTS ix_mandate_signers_user_id
  ON admin.mandate_signers(user_id);

-- D2 — varios mandatarios activos por (OT, compañía).
DROP INDEX IF EXISTS admin.uq_mandate_signer_companies_active;
CREATE UNIQUE INDEX IF NOT EXISTS uq_mandate_signer_companies_active
  ON admin.mandate_signer_companies(transit_office_id, company_tenant_id, mandate_signer_id)
  WHERE is_active;
```

### 5.2 `42-HU109xx-transit-office-mandate-config.sql`

```sql
CREATE TABLE IF NOT EXISTS admin.transit_office_mandate_config (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_transit_office_mandate_config PRIMARY KEY (id),
    transit_office_id uuid NOT NULL UNIQUE
        REFERENCES catalogs.transit_offices(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    -- Variante de plantilla; CHECK cerrado (patrón 33-F05/34-F05): añadir una exige tocar código.
    template_code varchar(30) NOT NULL DEFAULT 'generico'
        CONSTRAINT ck_tomc_template CHECK (template_code IN ('generico','sabaneta','bello')),
    requires_for_natural_person boolean NOT NULL DEFAULT false,
    institutional_mandatary_name varchar(200),
    institutional_mandatary_nit varchar(20),
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(), created_by uuid,
    updated_at timestamptz, updated_by uuid
);
-- Seed: Sabaneta (PN incluido, UT-SETSA) y Bello (UT-MAB). Resto = fila ausente ⇒ genérico + solo PJ.
```

Sin RLS: es catálogo de OT, no dato de tenant (mismo criterio que `catalogs.transit_offices`). Auditoría con los triggers estándar.

### 5.3 `43-…` — firmante elegido + matrices

```sql
ALTER TABLE tramites.procedure_instances ADD COLUMN IF NOT EXISTS mandate_signer_id uuid;
```
+ INSERT idempotente de `mandato` y `tramite_virtual` en `tramites.procedure_document_requirements` para `MATRICULA_NUEVA` y `TRASPASO` (`is_mandatory=false`, autogenerados; `default_sort_order` acordado con negocio), replicando el patrón de `24-HU10522-matricula-matrix-seed.sql`.

---

## 6. Backend por bloque

### 6.1 Mandatario: correo + identidad (reuso puro)
- `MandateSigner` / `MandateSignerItem` / `CreateMandateSignerRequest` / `UpdateMandateSignerRequest`: `+ DocumentType`, `+ Email`, `+ SignatureVaultId`, `+ IdentityValidationRef`, `+ UserId` (cuenta de OT, D9), `+ IdentityStatus` (proyectado).
- **Lista de usuarios de OT candidatos** para el selector del alta: reutilizar el listado de usuarios del tenant/OT ya existente (RBAC) — el FE lo consume para asignar la cuenta al mandatario.
- `AdminIdentitySubjectTypes` **`+ MandateSigner`**.
- **Endpoints nuevos** (copia literal de `AdminLegalRepresentativeIdentityEndpoints.cs`, incluidos los 422/502/503 y el "no exponer PII"):
  `POST /api/v1/admin/transit-offices/{otId}/mandate-signers/{id}/identity/send` y `…/resend`.
- `CreateMandateSignerHandler`: tras persistir, si hay correo → `SendAsync` (best-effort; un fallo del proveedor **no** revierte el alta, se reporta como advertencia).
- `MandateSignerSignatureResolver`: mismo contrato/precedencia (baúl > identidad) que `LegalRepresentativeSignatureResolver`, llaveado por el documento del mandatario firmante ya elegido (no por NIT de compañía).

### 6.2 Exclusividad → multiplicidad (D2)
- `CreateMandateSignerHandler` / `UpdateMandateSignerHandler`: quitar la validación de "compañía ya tomada".
- `IMandateSignerReader`: `ListActiveCompanyResolutionsAsync` pasa a devolver **N por compañía** ⇒ nuevo read model `MandateSignerCompanyResolution[]` agrupado; revisar los 3 tests de `MandateSignerUsageAndViewTests`.
- Añadir `ListActiveByOtAndCompanyAsync(transitOfficeId, companyTenantId)` — lo consumen §6.4 y §6.5.

### 6.3 Aplicabilidad del mandato (OT × persona)
- Nuevo puerto en `Flit.Tramites.Domain/Integration/`: `IMandateRequirementPolicy.ResolveAsync(transitOfficeId, ct) → MandateOtConfig?` (mismo patrón que `ISignatureVaultPolicy`/`IRnmcRequirementPolicy`, con un `NullMandateRequirementPolicy` seguro para tests). Adaptador en `Flit.Infrastructure/OtRules/`.
- `TramiteDocumentContext` **`+ ExigeMandato`** (bool, default `false` ⇒ checklist actual intacto), calculado como `EsNit || (EsPersonaNatural && config.RequiresForNaturalPerson)`.
- `ConditionalDocumentRules.Comunes()` **`+`** regla `mandato_autogenerado` (`ConditionalEffect.Add`, `obligatorio=false`, `docTipo="mandato"`): el documento aparece en el checklist como generado por el sistema, no como carga del cliente.

### 6.4 Generadores
Contratos nuevos en `Flit.Tramites.Application/Documents/IFurDocumentGenerator.cs` (mismo archivo donde ya conviven RUES/RNMC):

```csharp
public sealed record MandatoData(FurDocumentData Tramite, MandanteDatos Mandante,
    MandatarioDatos? Mandatario, string TemplateCode, bool FirmasVisibles, …);
public interface IMandatoGenerator { GeneratedDocument GenerateMandato(MandatoData data); }

public sealed record SolicitudVirtualData(FurDocumentData Tramite, FirmanteDatos Firmante, bool FirmasVisibles);
public interface ISolicitudVirtualGenerator { GeneratedDocument GenerateSolicitudVirtual(SolicitudVirtualData data); }
```

Implementaciones en `Flit.Infrastructure/Documents/`: `MandatoPdfGenerator.cs` (+ `Mandato/SabanetaLayout.cs`, `BelloLayout.cs`, `GenericoLayout.cs`) y `SolicitudVirtualPdfGenerator.cs`. `MandateTemplateResolver` (Domain, puro): `(templateCode, tipología, esPJ) → variante`.

Correspondencia con el legacy (verificada archivo por archivo):

| Variante | Matrícula | Traspaso | Particularidad |
|---|---|---|---|
| `sabaneta` + PN | `…-mandated-natural-persons-renting-sabaneta.hbs` | ídem transfer | Mandatario **institucional**; **solo firma el mandante**; bloque HASH opcional |
| `sabaneta` + PJ | `…-mandated-renting-sabaneta.hbs` | ídem transfer | Institucional; párrafo variable (`paragraphSabaneta`) |
| `bello` | `…-mandated-bello.hbs` | ídem transfer | Mandatario **persona** + RL de UT-MAB; 3 ramas: tramitador / PJ / PN |
| `generico` | `…-mandated.hbs` | ídem transfer | Mandatario **persona**; mismas 3 ramas; cita Res. 12379/2012 y 20233040017145/2023 |

`tramite_virtual`: una sola maqueta, 3 ramas de firmante (tramitador / RL de PJ / PN) y bloque final con NOMBRE / documento / CELULAR / CORREO.

### 6.5 Orquestación en `GenerarFurHandler` (FurCommand.cs)
Se añaden dos bloques con la **misma forma** que el de `certificado_rues` (generar-o-limpiar, idempotente):

1. `tramite_virtual` — **siempre** (persona natural **y** jurídica; para PN es el único documento de firma que entra al consolidado).
2. `mandato` — solo si `ExigeMandato`. Resuelve el/los mandatario(s) con `ListActiveByOtAndCompanyAsync`:
   - **0** → genera sin bloque de mandatario + advertencia (RF26 ya contempla "solo advierte");
   - **1** → firma automática si el estado ≠ `borrador` (D5: se persiste `mandate_signer_id`);
   - **>1** → **filtra por `user_id` = usuario autenticado** (D9); si hay **match** → lo setea y firma automáticamente; si **no hay match** → genera **sin** firma del mandatario y marca el trámite como *pendiente de selección al aprobar*.
   - Si deja de aplicar (regeneración tras cambiar de OT o de persona) → se **borra** el adjunto previo, igual que hace RUES hoy.

`FirmasVisibles = instance.Status != TramiteEstado.Borrador` (punto 18 del requerimiento).

### 6.6 Selección del firmante al aprobar
- `TramiteTransitionCommand` **`+ MandateSignerId?`**; `POST /instances/{id}/transition` acepta el campo.
- `TramiteLifecycleService`, en `entregado → aprobado`: si el mandato aplica, hay >1 mandatario activo, **el filtro por `user_id` (usuario autenticado) no dio match** y no llega `mandateSignerId` → error `mandatario_requerido` (ProblemDetails, 409), que el FE traduce a un selector. Con **1** mandatario o **match por `user_id`** → resuelve solo sin pedir nada. El `changedByUserId` que ya recibe la transición es el `user_id` a cotejar.
- Tras fijar el firmante: **regenerar el mandato** y **invalidar el consolidado** (`ConsolidadoMaestroVigente = false`) — la cascada ya existe (commit `0acfe371`, HU #10860).

---

## 7. Frontend

- **Admin OT → Mandatarios** (`frontend/components/admin/transit-offices/MandatarioFormPanel.tsx`, `MandatariosSection.tsx`):
  - `+` campos **tipo de documento** y **correo**;
  - `−` deshabilitar compañías "ya tomadas" (D2);
  - `+` columna **Identidad** con badge (`sin enviar` / `enviado` / `aprobado` / `vencido`) y botón **Reenviar correo**, reusando el patrón visual de `LegalRepresentativesTab.tsx` (HU #10904, misma rama).
- **Admin OT → ficha del OT**: sección **Mandato** (plantilla, "exige mandato a persona natural", mandatario institucional) — solo SuperAdmin.
- **Trámite → aprobar**: cuando la API responda `mandatario_requerido`, abrir un diálogo con los mandatarios activos y reintentar la transición con el elegido.
- **Trámite → documentos**: `mandato` y `tramite_virtual` aparecen como generados por el sistema (mismo tratamiento visual que FUR / certificado RUES). Nada que hacer si se siembran en la matriz.
- `flit-design-guardian` es obligatorio en todo lo anterior.

---

## 8. Mapa de archivos

**Crear**
```
services/core-api/src/Flit.Infrastructure/Persistence/Sql/Ddl/41-…-mandate-signers-identity.sql
services/core-api/src/Flit.Infrastructure/Persistence/Sql/Ddl/42-…-transit-office-mandate-config.sql
services/core-api/src/Flit.Infrastructure/Persistence/Sql/Ddl/43-…-mandate-signer-instance-matrix.sql
services/core-api/src/Flit.Api/Endpoints/AdminMandateSignerIdentityEndpoints.cs
services/core-api/src/Flit.Admin.Application/Companies/MandateSigners/MandateSignerSignatureResolver.cs
services/core-api/src/Flit.Tramites.Domain/Integration/IMandateRequirementPolicy.cs
services/core-api/src/Flit.Tramites.Domain/Tramites/Catalog/MandateTemplateResolver.cs
services/core-api/src/Flit.Infrastructure/OtRules/MandateRequirementPolicy.cs
services/core-api/src/Flit.Infrastructure/Documents/MandatoPdfGenerator.cs (+ Mandato/*Layout.cs)
services/core-api/src/Flit.Infrastructure/Documents/SolicitudVirtualPdfGenerator.cs
services/core-api/docs/adr/ADR-0036-mandatarios-multiples-y-mandato-por-ot.md   (supersede ADR-0023-firmante-mandato-exclusividad-modelo)
frontend/components/admin/transit-offices/MandateConfigSection.tsx
frontend/components/operacion/MandatarioSelectDialog.tsx
```

**Modificar**
```
Flit.Infrastructure/Persistence/Entities/Admin/MandateSigner.cs
Flit.Admin.Domain/Companies/MandateSigners/{MandateSignerReadModels,IMandateSignerReader}.cs
Flit.Admin.Application/Companies/MandateSigners/**  (Create/Update handlers + requests)
Flit.Admin.Domain/Identity/AdminIdentityValidation.cs   (AdminIdentitySubjectTypes + MandateSigner)
Flit.Tramites.Domain/Tramites/ValueObjects/TramiteDocumentContext.cs
Flit.Tramites.Domain/Tramites/Catalog/ConditionalDocumentRules.cs
Flit.Tramites.Application/Documents/IFurDocumentGenerator.cs
Flit.Tramites.Application/UseCases/ProcedureInstances/FurCommand.cs
Flit.Tramites.Application/UseCases/ProcedureInstances/Estados/{TramiteLifecycleService,TransitionProcedureInstanceCommand}.cs
frontend/components/admin/transit-offices/{MandatarioFormPanel,MandatariosSection}.tsx
frontend/lib/api/admin-mandate-signers.ts
```

---

## 9. Descomposición sugerida (8 HUs bajo un Feature nuevo)

| # | HU | Tipo | SP | Depende de |
|---|---|---|---|---|
| 1 | Modelo: correo/identidad/**cuenta de usuario (`user_id`, D9)** del mandatario + multiplicidad por compañía (migraciones + entidades + repos) | BACKEND | 5 | — |
| 2 | Validación de identidad del mandatario por correo: envío al registrar + reenvío (`subject_type='mandate_signer'`) | BACKEND | 3 | 1 |
| 3 | Configuración de mandato por OT (tabla + puerto + seed Sabaneta/Bello) | BACKEND | 3 | — |
| 4 | Regla de aplicabilidad OT × PN/PJ en el checklist condicional | BACKEND | 3 | 3 |
| 5 | Generador **Solicitud de trámite virtual** (2 variantes de firmante) + enganche en `GenerarFurHandler` | BACKEND | 5 | — |
| 6 | Generador **Mandato** (4 variantes × 2 modalidades) + enganche condicional + limpieza en regeneración | BACKEND | 8 | 3,4 |
| 7 | Selección del mandatario firmante al aprobar (transition + regeneración + invalidación de consolidado) | BACKEND | 5 | 1,6 |
| 8 | Admin OT: correo/identidad/multiplicidad + **selector de cuenta de usuario del mandatario** + sección Mandato + diálogo de selección al aprobar | FRONTEND | 8 | 2,3,7 |

Además: sembrar ambos tipos en las matrices documentales (dentro de la HU 5/6) para que el orden del consolidado no los mande a "Anexos".

---

## 10. Riesgos y duda menor pendiente

1. **Derogar ADR-0023** (exclusividad de un mandatario activo) es una decisión arquitectónica con datos ya en producción: el índice único se elimina, no hay migración de datos, pero **`ListActiveCompanyResolutionsAsync` cambia de cardinalidad** y su consumidor (vista RF34) debe revisarse.
2. **Texto legal.** El mandato es un contrato: cualquier deriva respecto al `.hbs` legacy es un riesgo jurídico. Se propone transcripción literal + revisión funcional del PO antes de mergear la HU 6.
3. **Sabaneta hardcodea "MATRICULA INICIAL"** en la plantilla de PN de matrícula y usa `paragraphSabaneta` en la de PJ; el traspaso tiene su propio archivo. Hay que decidir si el párrafo variable se parametriza por tipología o se replica literal por archivo.
4. **Datos que el legacy asume y FLIT 2.0 puede no tener capturados**: `vehicleOwnerAddress/City/Phone` del mandante para el pie de la solicitud virtual, y `ownerIsProcessor` (rama tramitador). Verificar cobertura contra `procedure_instance_actors` antes de la HU 5.
5. **Proveedor de identidad (Kyverum) en el flujo admin**: HU #10907 acota el riesgo con referencia externa opaca, pero la aprobación llega por **reconciliación**, no por webhook. Un mandatario recién enviado no queda aprobado hasta reconciliar — la UI debe reflejarlo (badge "enviado").
6. **Advertencia por ausencia de firma del mandatario** (0 mandatarios o ninguna firma/identidad vigente): decidir cómo se muestra (banner en el trámite vs. mensaje en la generación) — no bloquea (D6).
7. **Vínculo mandatario↔usuario obligatorio para la auto-firma (D9).** Un mandatario sin `user_id` nunca hará match automático ⇒ siempre caería en selección manual. En el alta del mandatario hay que **ofrecer elegir la cuenta de usuario de OT** (o dejarlo vacío conscientemente). Además: al **borrar el usuario**, el `ON DELETE SET NULL` deja el mandatario sin cuenta (se comporta como "sin match", correcto).

---

## 11. Gate / próximos pasos

Plan en estado **Propuesto**. Preguntas de negocio **cerradas** (R1–R4 + modelo de mandatario §4.3 + D9 enlace por `user_id`). Antes de escribir una línea de código:

1. **ADR-0036** (mandatarios múltiples + mandato configurable por OT, supersede ADR-0023-firmante-mandato-exclusividad-modelo) **ACEPTADO por el Líder Técnico (2026-07-24)**. ADR-0023 marcado como *superseded*. *(ADR-0035 ya ocupado por compraventa autogenerada del Feature #10852.)*
3. `feature-creator` → Feature en ADO (sprint siguiente al activo, `DOR`, AssignedTo humano) → `/decompose-feature` según §9.
4. Activación **una por una** de las HUs con confirmación explícita (gate humano, CLAUDE.md §2).
5. Rama de trabajo: partir de `feature/AB-10899-representantes-escrituras` (o de `develop` una vez integrada), **nunca** de la rama activa `feature/AB-10852-expediente-documental`.
