# ADR-0025 — Baúl de firmas: custodia de firmas precargadas y su consumo en trámites

- **Estado**: Aceptado · 2026-07-07 (aceptado por el responsable técnico del proyecto; D3 confirmado por el PO)
- **Módulo**: Admin Compañías (Baúl de Firmas) + Trámites (consumo en el paso de identidad/firma)
- **Requerimientos**: R13 (firma precargada por defecto para apoderados), R14 (validación automática por baúl o identidad vigente)
- **Decide**: **Líder Técnico** (custodia de material criptográfico — regla FLIT 15) + **PO** (por D3, catálogo de tenants)

## Contexto

Ciertas compañías gestoras operan con **apoderados** (representante legal de entidades tipo Renting/Leasing/Bancolombia) que firman en representación. Hoy cada trámite exige validación de identidad o firma del actor. El negocio pide (R13) ofrecer **por defecto** una **firma digital precargada** para esos apoderados, custodiada en un **baúl** por compañía, y (R14) **no exigir** una nueva validación cuando el actor ya tiene una firma activa y vigente en el baúl.

### Qué existe hoy en FLIT 2.0 (auditoría de código 2026-07-07)

- **El flag ya existe end-to-end pero es inerte:** `TenantSettings.SignatureVaultEnabled` (`Flit.Admin.Domain/Companies/Settings/TenantSettings.cs:22`, default `false`) ↔ columna `admin.tenant_operational_policies.signature_vault_enabled` ↔ wire `baulFirmasActivo`. Editable en el admin (`ConfiguracionEmpresaTab.tsx:40-46`, toggle "Baúl de firmas activo"), auditado (`SettingsDiff.cs:25`). **Ningún handler de runtime lo lee.**
- **Dos agregados de firma/identidad por trámite** (hijos de `ProcedureInstance`, por `Parte`):
  - `ProcedureInstanceBiometricValidation` (identidad biométrica Kyverum; vigencia **30 días** `BiometricRules.VigenciaDias`; `CertificateHash` = `firmaSerie`, HU #10488).
  - `ProcedureInstanceSignature` (firma electrónica; `Proveedor="mock"`, ZapSign diferido; solo `traspaso_standard`).
- **`EnsureIdentity`** (`EnsureIdentityCommand.cs`) ya resuelve la reutilización de identidad vigente (outcomes `ya_vigente`/`reusada`, reuso **por referencia** por documento) — **esta es la mitad de R14 ya entregada**.
- **Consumo en el FUR:** `FurCommand.cs` arma **sellos de texto** (identidad + firma) y `FurFieldMapper.SetSignature` **puede renderizar una imagen de firma** desde `FurDocumentData.FirmaImagenes` (byte[]), pero **ese camino está sin alimentar** (dead code): es la costura natural para estampar la firma del baúl.
- **Modelo cercano `MandateSigner`** (ADR-0023): el firmante de mandato, **de ámbito OT**, sin material de firma (su hash es integridad, no cripto).
- **Storage:** `IAttachmentStorage` → `FileManagerAttachmentStorage` (S3 vía URLs presignadas, calcula SHA-256, persiste solo el path). Generación documental con **QuestPDF** (`Infrastructure/Documents/`).
- **Catálogo de tenants bloqueado:** `CompanyTenantTypes` + CHECK `ck_tenants_tenant_type` = `RENTING/CONCESIONARIO/FLIT`. **Bancolombia/Leasing no están modelados.**
- **Patrón de config a espejar:** `admin.ot_requirements` + policy ports `IIdentityValidationPolicy`/`IRnmcRequirementPolicy` (Domain, desacoplados de Admin) + impl en Infrastructure + toggles en `RequirementsSection.tsx`.
- Prototipo huérfano `frontend/components/atom/StepperForm.tsx` (chip decorativo "Baúl Corporativo"), a reemplazar.

### Referencia analizada (otro proyecto)

Se analizó una implementación real de baúl (`D:\FLIT\BackCrudTransfer_master` + `D:\FLIT\Front\FrontNextJS`). Aprendizajes y **debilidades a NO copiar**:
- Baúl = registro por **NIT de empresa** del representante legal, con **ventana de vigencia**, hash, e imágenes (firma/huella/PNG compuesto) en **S3**. CRUD con 3 métodos de captura (pantalla / pad / subir PNG) + generación de un **PNG compuesto**.
- Consumo por **parámetros de empresa** con prioridad **baúl → identidad vigente → manual**; si aplica, **salta el paso de firma**.
- ⚠️ Debilidades de la referencia: **no cifra** el material de firma (confía en presigned URLs), la **vigencia es solo visual** (no se enforce en backend), el aislamiento es por **string** (sin RLS), y el firmado por-documento **guarda la llave privada junto a la firma**. Este ADR corrige las cuatro.

## Decisiones de producto ya acordadas (entradas)

1. **Apoderado ortogonal (D3):** NO se amplía el catálogo enforced de tenants. Se modela la condición "compañía con apoderado / firma precargada habilitada" como **atributo ortogonal** (config por compañía), no como nuevo `tenant_type`.
2. **Precedencia (D8):** en la validación del actor, el orden es **1) firma de baúl vigente → 2) identidad vigente reutilizable → 3) validación/firma manual**.
3. **Métodos de captura v1 (D9):** **firmar en pantalla** + **subir PNG precargado**; el **pad de hardware** queda para una fase posterior (requiere hardware).
4. **Vigencia enforced:** la ventana `vigencia_desde/hasta` **se valida en backend** (como los 30 días de identidad), no es solo visual.
5. **Storage reutilizado:** el artefacto de firma se persiste vía `IAttachmentStorage` (S3), guardando en la fila del baúl solo `storage_path` + `sha256`.
6. **Aislamiento por tenant con RLS** (a diferencia del ámbito OT de `MandateSigner`).

## Decisión

### 1. Apoderado como atributo ortogonal (alternativa recomendada)

La habilitación del baúl por compañía se gobierna con el flag **ya existente** `SignatureVaultEnabled` (activándolo) más, si el negocio lo requiere, un marcador de "compañía apoderada". **No** se toca el CHECK `ck_tenants_tenant_type` ni se introduce `BANCOLOMBIA`/`LEASING` como tipos. El baúl **puede** referenciar opcionalmente un `mandate_signer_id`/`company_tenant_id` (ADR-0023) cuando el apoderado coincida con un firmante de mandato, pero **no** depende de él.

> **Justificación:** ampliar el catálogo enforced tiene impacto de datos y de dominio (todo el sistema asume 3 tipos); el atributo ortogonal es reversible y localizado. Si el PO exige modelar Bancolombia/Leasing como tipo de tenant, se adopta la **Alternativa B**.

### 2. Modelo de datos (schema `admin`, RLS por tenant)

**`admin.signature_vault`** — la firma precargada del apoderado:

| Columna | Tipo | Nota |
|---|---|---|
| `id` | uuid PK | |
| `tenant_id` | uuid | **RLS** (`app.current_tenant_id`, patrón `procedure_instances`) |
| `document_type` | text | tipo de doc del firmante (apoderado/RL) |
| `document_number` | text | **PII (Ley 1581)** — no loguear, no exponer en errores |
| `nit_empresa` | text | NIT de la compañía (clave de búsqueda del consumo) |
| `full_name` | text | nombres y apellidos del firmante |
| `signature_hash` | text | huella de integridad de la firma (`SHA-256`) |
| `storage_path` | text | path del artefacto (PNG/PDF) en S3 vía `IAttachmentStorage` |
| `storage_sha256` | text | integridad del artefacto |
| `estado` | text | `activa` \| `revocada` (baja lógica explícita) |
| `vigencia_desde` | date | inicio de vigencia |
| `vigencia_hasta` | date | fin de vigencia — **enforced** |
| `mandate_signer_id` | uuid null | FK opcional → `admin.mandate_signers` (si aplica) |
| `created_at/by`, `updated_at/by`, `row_version` | | auditoría + concurrencia optimista |

- **Estado explícito** `activa`/`revocada` (mejor que el soft-delete-por-fecha de la referencia).
- **Índice** por `(tenant_id, nit_empresa, estado)` para el consumo; a lo sumo **una firma `activa` vigente** por `(tenant_id, nit_empresa, document_number)` (índice único parcial filtrado por `estado='activa'`).
- El **material de firma nunca se guarda en la fila**: vive en S3 (cifrado, ver §3); la fila solo referencia `storage_path`.

### 3. Custodia y cifrado (núcleo de seguridad)

- **Cifrado en reposo** del artefacto de firma: el bucket/objeto se cifra (SSE-KMS gestionado por el proveedor de storage) **y**, si el ADR de infraestructura lo exige, cifrado a nivel de aplicación de `storage_path`/material sensible antes de subir. **Nunca** se persiste material criptográfico en la BD en claro.
- **No exponer material en la API:** las respuestas del CRUD del baúl **no devuelven** el binario ni URLs públicas; la visualización usa el proxy de storage con **URLs presignadas de vida corta** (patrón `IAttachmentStorage`), gated por autorización.
- **Sin llave privada junto a la firma:** se rechaza explícitamente el patrón de la referencia (llave privada persistida). Si en el futuro se firma criptográficamente el PDF, la custodia de llaves va a **KMS/HSM**, nunca a una columna.
- **Enforce de vigencia + revocación inmediata:** el consumo valida `estado='activa'` **y** `now ∈ [vigencia_desde, vigencia_hasta]` en hora Colombia (UTC-5), reusando la utilería de `BiometricRules`. Revocar (`estado='revocada'`) surte efecto inmediato en el siguiente consumo.
- **PII:** `document_number` no se loguea ni se audita en claro (patrón `MandateSigner`).
- **Auditoría de uso (no-repudio):** cada vez que un trámite **consume** una firma del baúl, se registra el evento (trámite, actor, `signature_vault.id`, timestamp) para trazabilidad.

### 4. Consumo en trámites (activa el flag inerte)

- **Nuevo policy port** `Flit.Tramites.Domain/Integration/ISignatureVaultPolicy.cs` (+ `NullSignatureVaultPolicy` seguro) e impl en `Flit.Infrastructure` que lee `signature_vault_enabled` y resuelve la firma vigente por `(tenant, nit_empresa)`; registrado en `AdminInfrastructureExtensions.cs`. **Esto activa `SignatureVaultEnabled`.**
- **`EnsureIdentity`** (`EnsureIdentityCommand.cs`): nueva rama **antes** del reuso por documento — si el actor es **jurídico (NIT)**, el baúl está habilitado y hay firma **activa+vigente** → outcome **`firma_baul`** (no exige validación). Precedencia D8: baúl → `ya_vigente`/`reusada` → `requiere_validacion`.
- **`IdentityApprovalResolver`** (`ResolveApprovedPartiesAsync`/`ApprovedPartiesFromKeys`): la firma de baúl cuenta como "identidad aprobada" para la parte en el `SubmitGate` y el gate del FUR.
- **FUR:** alimentar `FurDocumentData.FirmaImagenes` (hoy sin productor) con el artefacto del baúl → `FurFieldMapper.SetSignature` renderiza la **imagen real** de la firma (en vez de solo el sello de texto).
- **Paso de firma (traspaso):** cuando el baúl aplica al apoderado, `SolicitarFirmaHandler` **omite** crear el sobre del proveedor y usa la firma precargada.

### 5. Admin — dónde se generan/gestionan las firmas

- **UI:** nueva **6ª pestaña "Baúl de Firmas"** en `CompanyConfigTabs.tsx` (o ruta `app/admin/companies/[tenantId]/signature-vault/`), renderizada como slot-panel (patrón `documentosSlot`/`otSlot`), **gated por el toggle `baulFirmasActivo` existente**. Reemplaza el prototipo huérfano `StepperForm.tsx`.
- **API:** `GET/POST/PUT/DELETE /api/v1/admin/companies/{tenantId}/signature-vault` (+ `POST .../{id}/revoke`), `SuperAdminPolicy`, con la auditoría por-campo del patrón `TenantSettingsRepository`.
- **Generación:** capturar (pantalla/subir PNG) → generar el artefacto (PNG compuesto opcional con firma+nombre+hash+vigencia, análogo a los generadores QuestPDF) → persistir vía `IAttachmentStorage.SaveAsync`.

## Alternativas consideradas

### Alternativa A — Apoderado ortogonal + baúl tenant-scoped con RLS (RECOMENDADA)
- (+) No toca el catálogo enforced ni el dominio existente; reversible; aislamiento real por tenant.
- (+) Activa el flag ya existente; espeja el patrón `ot_requirements`.
- (−) Un atributo/condición extra de compañía que mantener.
- Esfuerzo: **medio**. Riesgo: bajo.

### Alternativa B — Ampliar el catálogo de tenants (Bancolombia/Leasing)
- (+) Modela explícitamente las entidades del negocio.
- (−) Migración del CHECK `ck_tenants_tenant_type` + revisar todo el código que asume 3 tipos; impacto de datos.
- Esfuerzo: **medio-alto**. Riesgo: medio.

### Alternativa C — Extender `MandateSigner` para custodiar la firma
- (+) Reusa el firmante de mandato existente.
- (−) `MandateSigner` es **de ámbito OT** (no tenant) y por diseño **no** guarda material de firma; mezclaría responsabilidades y RLS. 
- Esfuerzo: **medio**. Riesgo: medio-alto (acopla dos dominios).

## Modelo de amenazas (resumen)

| Amenaza | Mitigación |
|---|---|
| Robo/lectura del material de firma | Cifrado en reposo (SSE-KMS), sin material en BD, URLs presignadas de vida corta, autorización SuperAdmin |
| Uso de firma vencida/revocada | Enforce de `estado='activa'` + vigencia en cada consumo; revocación inmediata |
| Suplantación (no-repudio) | Auditoría de cada uso (trámite+actor+firma+timestamp); `signature_hash` de integridad |
| Fuga de PII | `document_number` no logueado/expuesto; patrón `MandateSigner` |
| Cross-tenant | RLS por `tenant_id`; el consumo filtra por tenant + `nit_empresa` |
| Custodia de llaves (futuro firmado cripto) | KMS/HSM, nunca columna en BD (se rechaza el patrón de la referencia) |

## Consecuencias por agente

- **Backend:** tabla `admin.signature_vault` + migración EF con RLS; entidad/repo/config en `Persistence/…/Admin`; CRUD SuperAdmin; `ISignatureVaultPolicy` port + impl + DI; rama `firma_baul` en `EnsureIdentity`; integración en `IdentityApprovalResolver`, `SubmitGate`, `FurCommand`/`FurFieldMapper`; auditoría de uso.
- **Frontend:** 6ª pestaña "Baúl de Firmas" (CRUD, captura pantalla/subir PNG, listado por tenant, anular); opción "Firma Electrónica (baúl)" + auto-skip en el paso de identidad; reemplazo de `StepperForm.tsx`.
- **QA:** vigencia/revocación enforced; precedencia baúl→identidad→manual; solo NIT; RLS por tenant; no exposición de material; auto-skip cuando aplica.
- **Security:** cifrado en reposo, no exponer material, PII, auditoría de uso, KMS para llaves futuras. Revisar respuestas del CRUD.
- **Infra:** una migración nueva; configuración de cifrado del bucket/KMS; sin cambios de despliegue mayores.

## Requisito vs decisión (trazabilidad)

| Req | Estado con esta decisión |
|-----|--------------------------|
| **R13** (firma precargada por defecto para apoderados) | Cubierto: baúl tenant-scoped + CRUD admin + consumo por defecto para NIT habilitado |
| **R14** (validación por baúl **o** identidad vigente) | Cubierto: rama `firma_baul` en `EnsureIdentity` con precedencia D8 (la mitad "identidad vigente" ya estaba entregada) |
| D3 (catálogo de tenants) | Resuelto: **apoderado ortogonal** (Alternativa A); fallback = Alternativa B con OK del PO |

## Estado y aceptación

Este ADR fue **Aceptado** el 2026-07-07 por el responsable técnico del proyecto (regla FLIT 15), tras confirmar la decisión D3 (apoderado ortogonal) con el PO. La implementación del Feature #10641 (HUs #10642–#10647) procede sobre esta base. El material criptográfico se custodia con las mitigaciones del modelo de amenazas de este documento.
