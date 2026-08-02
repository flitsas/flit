# ADR-0033 — Directorio de representantes legales y escrituras por compañía gestora

- **Estado**: Aceptado · 2026-07-23 (aceptado por el Líder Técnico humano — regla FLIT 15)
- **Deciders**: Líder Técnico FLIT (aceptación exclusiva humana — regla FLIT 15)
- **Módulo**: Admin Compañías (nuevo directorio RL + escrituras) + Trámites (consumo en el wizard)
- **Requerimientos**: `RL-Escrituras-Firma.txt` — punto 1 (registro de RL), punto 2 (escrituras), punto 3 (consumo)
- **Tags**: arquitectura, backend, frontend, modulo-companias, multi-tenant

## Contexto

Cada **compañía gestora** (tenant) necesita mantener su propio directorio de **representantes legales** de las compañías con las que opera (identificadas por NIT — el propio o el de un tercero) y cargar sus **escrituras** (PDF con vigencia). En el registro del trámite, cuando el NIT consultado coincide con un registro del tenant, se debe **precargar** comprador/vendedor **sin** ir a RUES/RUNT, mostrar las escrituras vigentes en el primer paso, y **reutilizar** la firma del baúl o la validación de identidad del representante.

FLIT ya tiene las piezas de firma/identidad: el **baúl de firmas** (`admin.signature_vault`, ADR-0025) llaveado por `(tenant, nit_empresa, document_number)`; la **validación de identidad** reutilizable por documento (`FindVigenteApprovedByDocumentAsync`, HU #10350); la resolución de precedencia baúl→identidad (`IdentityApprovalResolver`); el catálogo `tramites.procedure_types` (TRASPASO/MATRICULA_INICIAL); y `IAttachmentStorage` (S3 presignado) para PDFs. Lo que **no existe** es el **directorio maestro** (compañía representada + representante + tipos de trámite que firma + escrituras) ni su consumo en el wizard. Este ADR decide **cómo modelarlo** sin duplicar la custodia de firma (que sigue en el baúl) y respetando el aislamiento multi-tenant. Es un concepto **distinto** del mandatario por OT (`MandateSigner`, ADR-0023): mismo apalancamiento de baúl/identidad, **llave diferente** (compañía vs compañía+OT).

## Decisiones de producto ya acordadas (entradas)

1. **Unicidad por tenant (D6):** el par `(NIT compañía, documento representante)` es único **dentro del tenant**, aislado por RLS. "Una compañía no puede usar los datos registrados por otra" = aislamiento estándar por tenant; cada gestora registra su propia copia. **No** hay exclusividad global cross-tenant.
2. **Consumo sin gate de rol (R3):** la precarga que evita RUES/RUNT aplica **siempre** que el NIT ingresado esté en el directorio del tenant, sin importar si el tenant es comprador o vendedor.
3. **Reutilización de firma/identidad:** al guardar un representante se vincula la firma del baúl activa+vigente o la validación de identidad vigente si existe; si no, el frontend ofrece enviar correo (ver [ADR-0034]) o registrar en el baúl. El consumo en el trámite se apalanca en `IdentityApprovalResolver` (no se reinventa).

## Decisión

Se crea un **directorio maestro normalizado, tenant-scoped con RLS**, en el schema `admin`, que **referencia** (no duplica) el baúl y la validación de identidad por la llave natural `(NIT, documento)`.

### Modelo de datos (schema `admin`, RLS por `app.current_tenant_id`)

- **`admin.represented_companies`** — dimensión de compañía representada. `id, tenant_id, document_type('NIT'), document_number (@pii:medium), name, email, address, city, phone, row_version, audit`. Único `(tenant_id, document_number)`.
- **`admin.company_legal_representatives`** — el registro = par compañía+representante. `id, tenant_id, represented_company_id (FK), document_type, document_number (@pii:high), first_last_name, second_last_name, name, email, address, city, phone, signature_vault_id (FK→signature_vault, null, ON DELETE SET NULL), identity_validation_ref (null), is_active, row_version, audit`. Único `(tenant_id, represented_company_id, document_number)` (D6).
- **`admin.company_legal_representative_procedure_types`** — puente M:N a `tramites.procedure_types` (marca "firma matrículas/traspasos", uno o varios). Único `(representative_id, procedure_type_id)`.
- **`admin.company_deeds`** — escrituras. `id, tenant_id, description, storage_path, storage_sha256, vigencia_desde, vigencia_hasta (CHECK ≥ desde), is_active, row_version, audit`.
- **`admin.company_deed_companies`** — puente escritura↔compañía representada. Único `(deed_id, represented_company_id)`.

`admin.signature_vault` **no cambia**: la firma del representante ya es su fila del baúl por NIT; solo se referencia desde `company_legal_representatives.signature_vault_id`.

### Consumo (trámites)

- **Escrituras vigentes por tenant** (collapse del primer paso): endpoint de lectura tenant-scoped que devuelve `[{nit, name, diasRestantes, vigenciaHasta}]`.
- **Lookup por NIT** (precarga comprador/vendedor): endpoint que, si hay match en el directorio del tenant, devuelve compañía + representante para precargar y **cortar** la consulta externa. La reutilización de firma/identidad la resuelve el `IdentityApprovalResolver` existente por `(tenant, NIT)` / documento.

## Alternativas consideradas

### Opción 1 — Directorio normalizado tenant-scoped que referencia el baúl (RECOMENDADA)
**Pros:** dimensión `represented_companies` hace triviales el multi-select de escrituras y la búsqueda por NIT; sin duplicación de datos de compañía; separa responsabilidades (directorio ≠ custodia de firma); RLS real; reusa baúl/identidad/`procedure_types` existentes.
**Cons:** más tablas (5) que mantener.
**Esfuerzo:** M. **Riesgos:** bajos.

### Opción 2 — Modelo denormalizado (compañía embebida en el representante)
**Pros:** menos tablas; formulario del punto 1 mapea 1:1 a una fila.
**Cons:** el multi-select de escrituras debe derivar `DISTINCT NIT` (inestable, sin id estable); datos de compañía duplicados cuando hay varios representantes del mismo NIT; edición de compañía inconsistente.
**Esfuerzo:** S-M. **Riesgos:** medios (integridad de datos).

### Opción 3 — Sobrecargar `admin.signature_vault` con los datos del directorio
**Pros:** una sola tabla; la firma y el directorio viven juntos.
**Cons:** rompe ADR-0025 (el baúl es **custodia de firma**, no directorio); mezcla PII/contacto con material de firma; el baúl puede no existir aún (representante sin firma todavía); complica RLS y auditoría; no modela escrituras ni tipos de trámite.
**Esfuerzo:** M. **Riesgos:** altos (acoplamiento, viola SRP de ADR-0025).

## Tradeoff aceptado

Se acepta el costo de 5 tablas nuevas (Opción 1) a cambio de un modelo limpio, con integridad referencial para el multi-select de escrituras y sin duplicar ni contaminar la custodia de firma del baúl. La Opción 2 se descarta por la inestabilidad del multi-select y la duplicación; la Opción 3 por violar la responsabilidad única del baúl (ADR-0025). La unicidad **por tenant** (D6) se elige por coherencia con el aislamiento multi-tenant y RLS ya establecido en todo el schema `admin`; la exclusividad global se descartó por romper ese patrón y por no ser necesaria para el caso de uso.

## Consecuencias

### Lo que se gana
- Precarga de comprador/vendedor por NIT que evita consultas externas (RUES/RUNT) y acelera el registro.
- Visibilidad de escrituras vigentes en el primer paso; reutilización automática de firma/identidad del representante.
- Base tenant-scoped reutilizable, desacoplada del mandatario por OT.

### Lo que se pierde
- Superficie de datos mayor (5 tablas + 2 endpoints de consumo).
- La compañía representada debe existir/actualizarse (upsert) al registrar un representante.

### Cambios operacionales
- Migraciones DDL crudas idempotentes (patrón `32-HU10642`) + entidades EF `ExcludeFromMigrations`; validar con `db-schema-validator` (RLS, triggers `row_version`/`audit`, índices, FKs).
- 2 endpoints de consumo tenant-scoped bajo `/api/v1/tramites/...`.

## ADRs relacionados

- [ADR-0025] — Baúl de firmas: se **referencia** (no se modifica) por `signature_vault_id`.
- [ADR-0023] (firmante de mandato) — concepto hermano por **OT**; este directorio es por **compañía**. Puerta abierta al mandatario (ver `docs/plan-tecnico-mandato-solicitud-virtual.md`).
- [ADR-0034] — validación de identidad admin desacoplada (usada por la acción "enviar correo" al guardar un representante sin firma/identidad).

## Notas para agentes

- **Database Agent**: 5 tablas en `admin` con RLS `set_config('app.current_tenant_id')`, triggers estándar, índices únicos (D6). DDL cruda + migración EF de acompañamiento. Marcar `@pii` en `document_number`/NIT.
- **Backend Agent**: agregados `RepresentedCompany`/`LegalRepresentative`/`Deed` + repos/readers **paginados** (envelope `{data,totalCount,page,pageSize}` de `ListCompanies`); resolución firma/identidad al guardar (D-entradas §3); endpoints CRUD SuperAdmin + 2 de consumo; puerto `IDeedDocumentStorage` sobre `IAttachmentStorage`.
- **Frontend Agent**: pestañas `isConfig:false` "Representantes" y "Escrituras" (patrón `signature-vault/`); precarga en `ActorsForm` **antes** de RUES/RUNT; collapse lazy de escrituras en `ConsultaStep`; badges `StatusBadge`.
- **QA Agent**: unicidad por tenant; RLS cross-tenant; precarga corta la consulta externa; vigencia de escrituras enforced; reutilización de firma/identidad en el trámite.
- **Security Agent**: PII (documentos/NIT), no exponer material de firma (solo referencia), URLs presignadas de vida corta para el PDF de escrituras, autorización SuperAdmin en el admin.
- **Infra Agent**: sin cambios mayores de despliegue; storage de PDFs vía `IAttachmentStorage` existente.

## Estado y aceptación

**Aceptado** el 2026-07-23 por el Líder Técnico humano (regla FLIT 15). La implementación del Feature (por crear) y sus HUs procede sobre esta base. D7 resuelto en la aceptación: modelo **normalizado** (Opción 1).
