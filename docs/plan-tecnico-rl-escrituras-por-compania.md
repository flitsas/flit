# Plan técnico — Representantes legales + Escrituras por compañía gestora

> **Origen:** `RL-Escrituras-Firma.txt` (raíz del repo) · **Fecha:** 2026-07-23 · **Estado:** Propuesto (pendiente aprobación humana)
> **Rol aplicado:** architecture-agent (desde el hilo principal). No inicia código: requiere descomposición en HUs y gates FLIT.
> **Alcance de esta iteración:** dejar **listo el RL por compañía** (llave = tenant + NIT de la compañía representada). El **mandante/mandatario** (llave = compañía + **OT**) queda con la **puerta abierta** — ver §10 y `docs/plan-tecnico-mandato-solicitud-virtual.md`.

---

## 1. Objetivo y alcance

Agregar información y funcionalidad **por compañía gestora** en tres bloques, tal como los define el requerimiento:

1. **Registro de representantes legales de la compañía** (CRUD en el admin de compañías): datos de la compañía representada (NIT) + datos del representante + marca de qué **tipos de trámite** puede firmar + vínculo a su **firma del baúl** o **validación de identidad** vigente.
2. **Carga de escrituras** (CRUD): documento **PDF** con descripción, **vigencia** (desde/hasta) y **multi-selección de compañías** a las que aplica (las mismas registradas en el punto 1).
3. **Consumo en el registro del trámite**: (a) collapse de escrituras vigentes en el **primer paso** del wizard; (b) en comprador/vendedor, si el NIT consultado coincide con un representante registrado del tenant, **precargar** y **NO** consultar RUNT/RUES; (c) **reutilizar** la firma del baúl o la validación de identidad del representante si están vigentes.

**Fuera de alcance (puerta abierta):** mandante/mandatario por **OT** (`MandateSigner`/`MandateSignerCompany`, ADR-0023) y los documentos autogenerados Mandato/Solicitud virtual — viven en el plan separado del mandato. Este RL **no** es el mandatario: son conceptos con **llaves distintas** (compañía vs compañía+OT) que **comparten** el baúl y la validación de identidad (§2, §10).

---

## 2. Conceptos y relación con lo existente (costuras validadas)

| Necesidad del requerimiento | Qué ya existe | Enganche |
|---|---|---|
| CRUD por compañía en el admin | Baúl de firmas (patrón end-to-end) | Copiar `signature-vault` (Domain agregado + reader/repo + Application Create/List/Get + `AdminSignatureVaultEndpoints.cs:20` bajo `/api/v1/admin/companies/{tenantId}/...` + entidad EF con RLS `set_config`). Montaje FE: tabs `isConfig:false` en `CompanyConfigTabs.tsx:19,40,179` + slots en `app/admin/companies/[tenantId]/page.tsx:121`. |
| Tabla **paginada** | Baúl **NO** pagina | Usar el envelope de compañías/auditoría: `{ data, totalCount, page, pageSize }` (`AdminCompaniesEndpoints.cs:238-249`, `GetTenantAuditLogResult.cs:7`). |
| PDF de escrituras (subir/ver) | `IAttachmentStorage` (S3/file-manager) | `IAttachmentStorage.cs:25` — `CreatePresignedUploadAsync` (subida directa a S3 de PDF grande, `:45`), `GetPresignedViewUrlAsync` (ver inline en navegador, `:70`), `OpenReadAsync` (`:62`). Envolver en un puerto acotado `IDeedDocumentStorage` (patrón `ISignatureVaultArtifactStorage.cs:16` / `SignatureVaultArtifactStorage.cs:16`). |
| Marca "firma matrículas/traspasos" | Catálogo `tramites.procedure_types` | Seed `TRASPASO`/`MATRICULA_INICIAL` (`20260622150000_SeedProcedureTypes.cs:25-28`), entidad `ProcedureType.cs`, catálogo `IProcedureTypeCatalog.cs:9`. Tabla puente M:N representante↔`procedure_type_id`. |
| Firma del baúl vigente por NIT | `ISignatureVaultReader.FindActiveByNitAsync` + `SignatureVault.EstaVigente` | `ISignatureVaultReader.cs:16`, `DbSignatureVaultReader.cs:21`, `SignatureVault.cs:171`. Baúl llaveado por `(tenant, nit_empresa, document_number)` = **exactamente** (NIT compañía, doc representante). |
| Validación de identidad vigente por documento | `FindVigenteApprovedByDocumentAsync` | `IProcedureInstanceRepository.cs:133` (reuso HU #10350). Vigencia = `validado_at` dentro de `BiometricRules.VigenciaDias` (30 días). |
| Reutilizar firma/identidad en el trámite | `IdentityApprovalResolver` ya resuelve por parte | `IdentityApprovalResolver.cs:24` con precedencia **(0) baúl por NIT** (`:38-43`, vía `ISignatureVaultPolicy.ResolveAsync`) → **(1)** fila propia → **(2)** identidad vigente por documento (`:57`). El registro del RL **no reinventa** la reutilización: se apalanca en esta cadena. |
| RL embebido en el actor (destino de la precarga) | `RepresentanteLegal` en `actor.metadata` | Tipo `RepresentanteLegal` (`procedure-runtime.ts:209-215`) dentro de `ProcedureActor.representanteLegal`; sección UI `ActorsForm.tsx:833-965`. |

**Conclusión:** el 70% de la infraestructura ya existe (baúl, storage, procedure_types, reutilización de identidad). Lo **nuevo** es el **directorio maestro** (compañía representada + representante + tipos de trámite + escrituras) y su **consumo** (precarga en el wizard).

---

## 3. Decisiones de diseño

### 3.1 Confirmadas por el enfoque del usuario
- **D1 — Concepto separado del mandatario.** El RL por compañía es una entidad nueva **tenant-scoped**, llave `(tenant, NIT compañía representada, documento representante)`. **NO** se toca `MandateSigner` (que sigue siendo compañía+OT). Convergen solo en baúl/identidad (§10).
- **D2 — La firma/identidad se resuelve al guardar (req. línea 39).** Al crear/editar un representante: buscar firma del baúl activa+vigente por `(tenant, NIT, docRepresentante)` **o** validación biométrica vigente por documento; si existe, **vincularla**; si no, devolver una señal para que el FE ofrezca **(a)** enviar correo de validación de identidad o **(b)** registrar en el baúl. Precedencia baúl > identidad (coherente con `IdentityApprovalResolver`).
- **D3 — Tipos de trámite (req. línea 40-41).** M:N a `tramites.procedure_types`; un representante firma **uno o varios** tipos. Semilla base: `MATRICULA_INICIAL`, `TRASPASO`.
- **D4 — Consumo que evita RUNT/RUES (req. línea 63).** En comprador/vendedor, antes de consultar RUES por NIT, buscar en el directorio del tenant; si hay match, **precargar** razón social + datos del RL y **cortar** la consulta externa.
- **D5 — Escrituras = PDF con vigencia + multi-compañía (req. líneas 48-56).** Reutilizar `IAttachmentStorage` (presigned upload/view). El multi-select de compañías se alimenta de las compañías representadas registradas en el punto 1.
- **D6 — Unicidad del par (compañía, representante) = POR TENANT (confirmado 2026-07-23).** Unicidad `(tenant_id, NIT, docRepresentante)` + aislamiento RLS. Cada gestora ve/usa solo lo suyo; "no usar los de otra" = aislamiento por tenant estándar. (Se descartó la exclusividad global cross-tenant.)
- **D8 — Correo de validación de identidad admin = EN ESTA ENTREGA, como bloque compartido (confirmado 2026-07-23).** Se construye la **validación de identidad desacoplada del trámite** (envío de correo → aprobación → vínculo), reutilizable también por el mandatario (§10). Depende de que Kyverum permita verificación sin trámite (R2). Es la HU-9 (ya **no** condicional).
- **R3/consumo — Precarga = SIEMPRE que haya match del tenant (confirmado 2026-07-23).** Si el NIT ingresado está en el directorio del tenant, se precarga y se corta la consulta externa, sin importar si el tenant es comprador o vendedor. El gate `onlyOwnVehicles` solo afecta el autorrelleno del propietario en el paso 1, no la precarga.

### 3.2 Abierta (recomendación por defecto — ver §11)
- **D7 — Modelo de la "compañía representada" (default: Opción A).**
  - **Opción A (recomendada, asumida en §4):** normalizar en `admin.represented_companies` (dimensión por NIT: nombre, email, dirección, ciudad, teléfono) que **comparten** representantes y escrituras. Hace trivial el multi-select de escrituras y la búsqueda por NIT.
  - **Opción B:** denormalizar (datos de compañía embebidos en cada representante) y derivar las compañías del multi-select por `DISTINCT NIT`. Menos tablas, más duplicación.

---

## 4. Modelo de datos (migraciones)

Todas idempotentes, patrón del repo: entidad EF con `ExcludeFromMigrations()` + **DDL SQL cruda** en `Persistence/Sql/Ddl/` (como `32-HU10642-signature-vault.sql`), RLS con `app.current_tenant_id`, triggers `row_version`+`audit`, PK `uuidv7()`. Validar con `db-schema-validator`.

Asumiendo **D7=A** (normalizado, default) y **D6=por tenant** (confirmado):

1. **`admin.represented_companies`** — dimensión de compañía representada.
   `id, tenant_id (FK identity.tenants, RLS), document_type ('NIT'), document_number (@pii:medium), name, email, address, city, phone, row_version, created_*/updated_*`.
   Único `(tenant_id, document_number)`.
2. **`admin.company_legal_representatives`** — el representante (registro = par compañía+representante).
   `id, tenant_id (RLS), represented_company_id (FK → represented_companies), document_type ('CC'…), document_number (@pii:high), first_last_name, second_last_name, name, email, address, city, phone, signature_vault_id (FK → admin.signature_vault, nullable, ON DELETE SET NULL), identity_validation_ref (nullable — id de la validación biométrica vinculada), is_active, row_version, created_*/updated_*`.
   Único `(tenant_id, represented_company_id, document_number)` (D6 por tenant, confirmado).
3. **`admin.company_legal_representative_procedure_types`** — puente M:N (marca de tipos de trámite).
   `id, representative_id (FK, ON DELETE CASCADE), procedure_type_id (FK → tramites.procedure_types), created_at`. Único `(representative_id, procedure_type_id)`.
4. **`admin.company_deeds`** — escrituras (PDF).
   `id, tenant_id (RLS), description, storage_path, storage_sha256, vigencia_desde date, vigencia_hasta date (CHECK hasta ≥ desde), is_active, row_version, created_*/updated_*`.
5. **`admin.company_deed_companies`** — puente escritura ↔ compañía representada.
   `id, deed_id (FK, ON DELETE CASCADE), represented_company_id (FK → represented_companies), created_at`. Único `(deed_id, represented_company_id)`.

**`admin.signature_vault` no cambia** (la firma del representante ya es su fila del baúl por NIT). Solo se **referencia** desde `company_legal_representatives.signature_vault_id`.

---

## 5. Backend por bloque

### 5.1 Punto 1 — Representantes (directorio + resolución de firma/identidad)
- **Domain** (`Flit.Admin.Domain/Companies/LegalRepresentatives/`): agregado `LegalRepresentative` (factory `Create`, `Rehydrate`, `Deactivate`, invariantes de datos) + `RepresentedCompany` (upsert por NIT) + `ISignatureVaultReader` (ya existe) + nuevos `ILegalRepresentativeRepository` / `ILegalRepresentativeReader` (con paginación).
- **Application** (`Flit.Admin.Application/Companies/LegalRepresentatives/`): `Create`, `Update`, `List` (paginado), `GetById`, `Delete/Deactivate`. En `Create/Update`:
  1. upsert de `RepresentedCompany` por `(tenant, NIT)`.
  2. **resolución de firma/identidad (D2):** `ISignatureVaultReader.FindActiveByNitAsync(tenant, NIT)` + `EstaVigente` → si hay, set `signature_vault_id`; si no, `FindVigenteApprovedByDocumentAsync(tenant, tipoDocRep, docRep)` → si hay, set `identity_validation_ref`; si ninguna, `Result` con flag `sin_firma_ni_identidad` (para que el FE muestre las 2 opciones).
  3. persistir tipos de trámite (bridge).
- **Endpoints** (`AdminLegalRepresentativesEndpoints.cs`) bajo `/api/v1/admin/companies/{tenantId:guid}/legal-representatives`, `SuperAdminPolicy`: `GET ""` (paginado `?page&pageSize`), `GET /{id}`, `POST ""` (201/422), `PUT /{id}`, `DELETE /{id}` (o `/{id}/deactivate`). 422 con sobre `{ errors: [{field,code,message}] }`.
- **Infra**: entidad + EF config (`ExcludeFromMigrations`, índices, FKs, RLS `set_config` como `SignatureVaultRepository.cs:137-164`), repo + reader (paginado), DI en `AdminInfrastructureExtensions.cs:92-99`.

### 5.2 Punto 1b — Correo de validación / registrar en baúl (D8, en esta entrega)
- **Enviar correo (validación desacoplada):** endpoint `POST /legal-representatives/{id}/identity/send` → inicia validación de identidad admin (proveedor Kyverum, envía el correo) y al aprobar vincula `identity_validation_ref`. Se implementa como **servicio compartido `IAdminIdentityValidationService`** desacoplado del trámite, para que el mandatario lo reutilice después (§10). Es la HU-9.
- **Registrar en baúl:** no requiere endpoint nuevo — enlaza a la UI del baúl existente (`SignatureVaultTab`/captura) prellenando NIT + documento; al volver, `Update` revincula.

### 5.3 Punto 2 — Escrituras
- **Storage**: `IDeedDocumentStorage` (puerto acotado) → delega en `IAttachmentStorage` (`tipo="escritura"`, `filename="escritura.pdf"`). Subida vía `CreatePresignedUploadAsync`; visualización vía `GetPresignedViewUrlAsync`.
- **Domain/Application**: agregado `Deed` + `Create/Update/List(paginado)/GetById/Delete` + validación de rango de vigencia + persistir puente compañías.
- **Endpoints** (`AdminDeedsEndpoints.cs`) bajo `/api/v1/admin/companies/{tenantId}/deeds`: `GET ""` (paginado), `GET /{id}` (+ URL presignada de vista), `POST ""` (crea + presigned upload o multipart), `PUT /{id}`, `DELETE /{id}`.

### 5.4 Punto 3 — Consumo (endpoints de lectura para el wizard)
- **Escrituras vigentes del tenant** (collapse paso 1): `GET /api/v1/tramites/deeds/active` (o dentro del wizard state) → devuelve `[{ nit, name, diasRestantes, vigenciaHasta }]` filtrado por tenant + `is_active` + vigencia. Tenant-scoped por el header `X-Tenant-Id`.
- **Lookup de representante por NIT** (precarga comprador/vendedor): `GET /api/v1/tramites/instances/{id}/legal-representative-lookup?nit=NNN` → si hay match para el tenant, devuelve `{ company: {razonSocial, ...}, representante: {tipoDoc, doc, nombres, email, telefono}, firmaVigente: bool, identidadVigente: bool }`; si no, `404/empty` (el FE cae a RUES/RUNT normal).

---

## 6. Frontend por bloque

### 6.1 Admin — pestaña **Representantes legales** (punto 1)
- Copiar el patrón `signature-vault/`: `LegalRepresentativesTab.tsx` (tabla **paginada** con `page/pageSize/total`, estados `UiStateBoundary`, botón "Nuevo representante"), `LegalRepresentativeFormPanel.tsx` (`OtSidePanel`; campos compañía + representante del JSON del req.; selector múltiple de tipos de trámite; al guardar, si `sin_firma_ni_identidad` mostrar las 2 acciones: "Enviar correo de validación" / "Registrar en baúl"), `LegalRepresentativeDetailModal.tsx`.
- Cliente `frontend/lib/api/admin-legal-representatives.ts` (copia de `admin-signature-vault.ts`, con paginación).
- Montaje: nuevo `TabId` + slot `isConfig:false` en `CompanyConfigTabs.tsx` + `legalRepresentativesSlot` en `app/admin/companies/[tenantId]/page.tsx:121`.
- Badges de estado firma/identidad con `StatusBadge.tsx:33` (tonos success/warning/danger).

### 6.2 Admin — pestaña **Escrituras** (punto 2)
- `DeedsTab.tsx` (tabla paginada), `DeedFormPanel.tsx` (descripción; carga de PDF; fechas desde/fin; **multi-select** de compañías representadas → alimentado por `admin-legal-representatives` / `represented_companies`; guardar). Cliente `admin-deeds.ts`. Visor del PDF vía URL presignada.

### 6.3 Wizard — consumo (punto 3)
- **Collapse de escrituras (primer paso):** en `ConsultaStep` (`TramiteWizard.tsx:1002`, cuerpo `:1301-1302`), insertar un colapsable **contraído por defecto** con **carga perezosa** (patrón `BiometricStep.tsx:722-758`). Datos: `GET deeds/active` para el tenant (NIT ya disponible: `tenantNitDigits` `TramiteWizard.tsx:1047-1049`). Proyecta NIT, nombre y **días restantes** por escritura con `StatusBadge` (verde/ámbar/rojo por umbral).
- **Precarga comprador/vendedor:** en `ActorsForm.handleIdentityLookup` rama jurídica (`ActorsForm.tsx:450-465`) y en el effect `autoConsultRunt` (`:526-548`): **antes** de `ruesPersonLookup`, llamar al **lookup por NIT**; si hay match, `updateActor(...)` (razón social + `tipoDocumento:'NIT'`) + `updateRepLegal(...)` (datos del RL) + `setRuntFor(index,{status:'found',kind:'rues',result:preload})` y `return` sin ir a RUES/RUNT. Gate: solo cuando el tenant autenticado es parte (política `onlyOwnVehicles`, `TramiteWizard.tsx:1082-1103`; claim `company_nit` en `jwt.ts`). La reutilización de firma/identidad **no** requiere trabajo extra en el wizard: `IdentityApprovalResolver`/`ensureIdentity` ya resuelven el baúl por NIT (`TramiteWizard.tsx:480-483`).

---

## 7. Mapa de archivos (crear/modificar)

**Backend — crear**
- `Flit.Admin.Domain/Companies/LegalRepresentatives/` (agregados `LegalRepresentative`, `RepresentedCompany`, `Deed` + repos/readers/read models).
- `Flit.Admin.Application/Companies/LegalRepresentatives/` y `.../Deeds/` (Create/Update/List/Get/Delete + storage port `IDeedDocumentStorage`).
- `Flit.Api/Endpoints/AdminLegalRepresentativesEndpoints.cs`, `AdminDeedsEndpoints.cs` + consumo en `Flit.Api/Endpoints/Tramites/` (deeds/active, legal-representative-lookup).
- `Flit.Infrastructure/Persistence/Entities/Admin/` (+ EF configs `ExcludeFromMigrations`), `.../Repositories/` (RLS scope), `.../Storage/DeedDocumentStorage.cs`.
- `Flit.Infrastructure/Persistence/Sql/Ddl/NN-…-legal-representatives.sql` y `…-deeds.sql` (DDL cruda) + migraciones EF de acompañamiento.

**Backend — modificar**
- `Flit.Api/Program.cs` (map endpoints), `Flit.Admin.Application/DependencyInjection.cs` + `AdminInfrastructureExtensions.cs:92-99` (DI).

**Frontend — crear**
- `frontend/components/admin/companies/legal-representatives/` (Tab/FormPanel/DetailModal), `.../deeds/` (Tab/FormPanel).
- `frontend/lib/api/admin-legal-representatives.ts`, `admin-deeds.ts`, y cliente de consumo (deeds activas + lookup por NIT) en `tramites-client.ts`.
- Componente colapsable de escrituras (reutilizando el patrón de `BiometricStep`).

**Frontend — modificar**
- `CompanyConfigTabs.tsx:19,40,179`, `app/admin/companies/[tenantId]/page.tsx:121` (tabs + slots).
- `TramiteWizard.tsx:1301-1302` (collapse), `ActorsForm.tsx:450-465,526-548` (precarga), `jwt.ts:11-23` (tipar `company_nit`).

---

## 8. Descomposición sugerida en HUs

> **Feature nuevo** (candidato: hijo de un Feature "RL + firma por compañía gestora" o del expediente documental #10852 si se prefiere agrupar). Sin dependencia de Branding (#10852): las escrituras se **suben/visualizan**, no se generan.

| HU | Tipo | Alcance | Depende de |
|----|------|---------|-----------|
| **HU-1** | DB+BE | Modelo base: `represented_companies` + `company_legal_representatives` + puente `procedure_types` + migraciones (RLS, índices, DDL) + resolución firma/identidad al guardar (D2). | — |
| **HU-2** | BE | Endpoints admin CRUD **representantes** (paginado) + Application + validación 422 + respuesta `sin_firma_ni_identidad`. | HU-1 |
| **HU-3** | BE | **Escrituras**: entidad + `IDeedDocumentStorage` (PDF presigned) + CRUD paginado + vigencia + puente compañías. | HU-1 |
| **HU-4** | BE | Endpoints de **consumo**: `deeds/active` (tenant) + `legal-representative-lookup?nit`. | HU-2, HU-3 |
| **HU-5** | FE | Admin: pestaña **Representantes** (tabla paginada, form compañía+RL, tipos de trámite, estados firma/identidad, acciones enviar correo / registrar baúl). | HU-2 |
| **HU-6** | FE | Admin: pestaña **Escrituras** (tabla paginada, form PDF+vigencia+multi-compañía, visor). | HU-3 |
| **HU-7** | FE | Consumo wizard: **collapse escrituras** en el primer paso (NIT/nombre/días con badges). | HU-4 |
| **HU-8** | FE | Consumo comprador/vendedor: **precarga por NIT** evitando RUES/RUNT + reutilización firma/identidad. | HU-4 |
| **HU-9** | BE | Validación de identidad **admin por correo** (`IAdminIdentityValidationService`, desacoplada del trámite), compartida con el mandatario. Consumida por HU-2/HU-5 (acción "enviar correo"). | HU-1 |

Cada HU cierra con `dev-tester` (evidencias PASO 6) y pasa gates FLIT (Active con confirmación, PR ≤800 líneas a `develop`, Code Review + Security, reviewer humano).

---

## 9. Riesgos y puntos abiertos

- **R1 — Unicidad (D6) → RESUELTO:** por tenant (RLS). Sin verificación cross-tenant.
- **R2 — Validación de identidad admin desacoplada (D8).** Depende de que Kyverum permita una verificación no anclada a un trámite (mismo R2 del plan de mandato). Si al implementar HU-9 se confirma que **no** lo permite, degradar a "registrar en baúl" y escalar el bloqueo. Riesgo **activo** (D8 ya está en alcance).
- **R3 — Gate del consumo → RESUELTO:** precargar **siempre** que el NIT esté en el directorio del tenant; `onlyOwnVehicles` solo afecta el autorrelleno del propietario en el paso 1.
- **R4 — Datos PII.** `document_number` del representante y NITs → marcar `@pii` y respetar RLS + auditoría (triggers estándar). No exponer material de firma (solo referencia de storage).
- **R5 — Colisión de la precarga con `autoConsultRunt`.** El vendedor auto-consulta RUNT al sembrarse desde el propietario (`ActorsForm.tsx:526-548`); la precarga por NIT debe **anteponerse** a ese effect para no disparar la consulta externa.
- **R6 — Fuente del multi-select de escrituras.** Depende de D7: con `represented_companies` (A) es directo; con denormalizado (B) hay que derivar `DISTINCT NIT` y la UI queda menos estable.

---

## 10. Puerta abierta — mandante/mandatario (compañía + OT)

Este plan deja el terreno listo para el mandatario sin acoplarse a él:
- **Llaves distintas:** RL por compañía = `(tenant, NIT)`; mandatario = `(tenant, OT)` (`MandateSignerCompany`). No se fusionan las tablas.
- **Building blocks compartidos:** (1) el **baúl de firmas** (ya llaveado por NIT) y (2) la **validación de identidad admin por correo** (D8 aquí = D1 del plan de mandato). Recomendación: implementar D8 como servicio reutilizable para que el mandatario lo consuma después.
- **Consumo documental:** cuando se retome el mandato, sus generadores (Mandato/Solicitud virtual) podrán resolver la firma del firmante con el **mismo** resolutor de precedencia baúl/identidad. Ver `docs/plan-tecnico-mandato-solicitud-virtual.md` (§4.4, D7).

---

## 11. Gate / próximos pasos

Plan en estado **Propuesto**. Decisiones confirmadas (2026-07-23): **D6** por tenant, **D8** correo de identidad en esta entrega (bloque compartido), **R3** precarga siempre que haya match del tenant. Queda solo **D7** con default recomendado (Opción A, normalizado). Antes de implementar:
1. (Opcional) Confirmar **D7** si se prefiere el modelo denormalizado; en su defecto se procede con Opción A.
2. **ADR redactados (Propuesto, 2026-07-23):** `ADR-0033` (directorio RL por compañía + escrituras, modelo D6/D7) y `ADR-0034` (validación de identidad admin desacoplada compartida, D8). Pendiente: aceptación humana del Líder Técnico (regla FLIT 15).
3. `feature-creator` (Fase 1 del flujo `/requirement-to-delivery`) → schema (Fase 2b, `database-agent`) → `/decompose-feature` según §8.
4. Activación de HUs una por una (gate humano) antes de codificar.
