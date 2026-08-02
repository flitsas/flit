# Plan técnico — Ajustes de Representantes, Escrituras y Baúl (RL-flujo-ajustes)

> **Origen:** correcciones del Líder Técnico sobre la entrega del Feature #10899 (representantes,
> escrituras y baúl de firmas). Fuente de requisitos: `RL-flujo-ajustes.txt`.
> **Base de código:** rama `feature/AB-10899-representantes-escrituras` (= `origin/develop`, ya incluye #10899 e #10852).
> **Estado:** solo plan. No implementado.

## 0. Decisiones cerradas

| # | Decisión | Resolución |
|---|----------|------------|
| D1 | Unificación de menú | **Representante-céntrico**: una sola vista del representante donde se anidan sus empresas y las escrituras de cada empresa; el baúl queda accesible desde ahí. |
| D2 | Selección de escritura en el trámite | **Más próxima a vencer** (menor `VigenciaHasta` entre las vigentes). Revierte ADR-0033/#10926 (mayor vigencia) → **actualizar ADR-0033**. |
| D3 | Campo hash del baúl | **Columna nueva** `codigo_hash` (código alfanumérico digitado por el usuario). Se conserva `signature_hash`/`storage_sha256` para integridad del artefacto. |
| D4 | Firma en el baúl | Se **deprecа el NIT**: la firma es exclusivamente de la persona (`document_type` + `document_number`) y pertenece al tenant. Una firma por persona. |
| D5 | Firma del trámite | La resuelve el **representante seleccionado** (por su documento), no el NIT de la empresa. Acoplado a D4 y al select del Bloque 3. |

---

## 1. Estado actual (mapa de código)

### 1.1 Baúl de firmas
- Tabla `admin.signature_vault` — `Persistence/Sql/Ddl/32-HU10642-signature-vault.sql:8-33`.
  - `nit_empresa varchar(20) NOT NULL` (:14) — **a deprecar**.
  - `signature_hash varchar(64) NOT NULL` (:16) = hash **calculado** del artefacto (no es el código que digita el usuario).
  - Unicidad activa: `uq_signature_vault_activa (tenant_id, nit_empresa, document_number) WHERE estado='activa'` (:43-45).
  - Índice de consumo `(tenant_id, nit_empresa, estado)` (:36-37).
  - Firma ya es por persona (`document_type`+`document_number`, :12-13) y por tenant (`tenant_id` + RLS, :52-54). ✔
- Entidad `SignatureVault.cs` (`EstaVigente`, `Revocar`).
- Lectura `DbSignatureVaultReader.FindActiveByNitAsync(tenant, nit)` — `:21-45`.
- Endpoints `AdminSignatureVaultEndpoints.cs` — base `/api/v1/admin/companies/{tenantId}/signature-vault`.
- FE: `SignatureVaultFormPanel.tsx` (pide NIT requerido, sin hash), cliente `admin-signature-vault.ts`.

### 1.2 Representante legal
- `admin.represented_companies` (compañía por NIT) — `Ddl/39-HU10900-legal-representatives-deeds.sql:14-31`.
- `admin.company_legal_representatives` — `:54-80`:
  - `represented_company_id NOT NULL` (:58-60) → **1 representante = 1 empresa por fila**.
  - Unicidad `(tenant_id, represented_company_id, document_number)` (:83-84).
  - `signature_vault_id` (:70-72) e `identity_validation_ref` (:73) **por fila** (por empresa).
  - Puente M:N `company_legal_representative_procedure_types` (:110-125).
- Entidad `LegalRepresentative.cs`: `SignatureVaultId`/`IdentityValidationRef` **excluyentes** (`LinkSignature`/`LinkIdentity`, :176-200; precedencia baúl > identidad).
- Escritura/lectura: `LegalRepresentativeRepository.cs`, `DbLegalRepresentativeReader.cs`, lógica común `LegalRepresentativeWriter.cs`.
- Endpoints `AdminLegalRepresentativesEndpoints.cs` (CRUD + `/procedure-types`), identidad `AdminLegalRepresentativeIdentityEndpoints.cs`.
- Decisión firma/identidad al guardar: `LegalRepresentativeSignatureResolver.ResolveAsync:37-76` (baúl por NIT con match de documento → identidad → señal `sin_firma_ni_identidad`).
- FE: `LegalRepresentativesTab.tsx`, `LegalRepresentativesFormPanel.tsx` (1 empresa, sin escrituras), cliente `admin-legal-representatives.ts`.

### 1.3 Escrituras (deeds)
- `admin.company_deeds` — `:141-157` (vigencia_desde/hasta, is_active, storage).
- M:N `admin.company_deed_companies (deed_id, represented_company_id)` — `:178-201`. **Escritura ↔ compañía**, no directamente al representante.
- Lectura `DbDeedReader.ListActiveVigentesAsync(tenant, today)` — `:78-99` (filtra vigentes, ordena asc por `VigenciaHasta`).
- **Historial ya soportado**: varias escrituras por compañía con vigencias distintas.
- FE: `DeedsTab.tsx`, `DeedsFormPanel.tsx` (multi-select de compañías), cliente `admin-deeds.ts`.

### 1.4 Menú admin
- Todo en `app/admin/companies/[tenantId]/page.tsx` → `CompanyConfigTabs.tsx` como **3 pestañas separadas** (`representantes`, `escrituras`, `baul`). El baúl solo aparece si `settings.baulFirmasActivo`.

### 1.5 Wizard de trámites
- Orden (server-driven) en `WizardStateQuery.StepKey:931-951`:
  - **Traspaso:** `consulta → documentos → vendedor → comprador → comercial → fur`.
  - **Matrícula:** `consulta_vin → documentos → comprador → identidad → fur`.
  - → **Documentos va ANTES de los actores.**
- Precarga de actor PJ: `ActorsForm.tsx handleIdentityLookup:498-566` → `lookupLegalRepresentativeByNit` (:511) devuelve **UN** representante; corta RUES/RUNT; muestra banderas firma/identidad vigentes. **No hay select**; no precarga escritura.
- Firma: compraventa informativa/no bloqueante (`FirmaFurStep.tsx`); identidad de PJ cubierta por baúl vía `SignatureVaultPolicy.ResolveAsync:31-71` (**por NIT**, firma activa+vigente).
- Escritura del consolidado: `ProcedureDeedResolver.ResolveForActorsAsync:40-122`; selección `:85-88` = `OrderByDescending(VigenciaHasta)` (**mayor vigencia**). Se adjunta como `ProcedureInstanceAttachment` (`escritura`/`escritura_comprador`, `Source="system"`) en `FurCommand.cs:201-232`.
- **No hay** campo en `ProcedureInstanceActor`/`ProcedureInstance` que referencie la escritura usada.

---

## 2. Brechas y cambios por bloque

### Bloque 1 — Baúl de firmas
| Requisito | Brecha | Cambio |
|---|---|---|
| NIT deprecado / fuera del form | `nit_empresa NOT NULL`, requerido en UI, base de unicidad y consumo | DDL: `nit_empresa` nullable + nueva unicidad `(tenant_id, document_number) WHERE estado='activa'` + índice de consumo por documento. Readers `FindActiveByNitAsync` → `FindActiveByDocumentAsync`. FE: quitar campo. |
| Input hash digitable | No existe (solo hash calculado) | DDL: columna `codigo_hash varchar(...)` nullable. Entidad + endpoint + input FE. |
| Firma del tenant | Ya cumple (`tenant_id`+RLS) | — |

### Bloque 2 — Representante + Escrituras
| Requisito | Brecha | Cambio |
|---|---|---|
| Representante → N empresas | 1 empresa por fila | Representante = persona única `(tenant, document_number)`; **nueva tabla puente** `admin.legal_representative_companies (representative_id, represented_company_id)`. Migrar filas actuales (colapsar por documento). |
| Una firma para todas las escrituras | firma/identidad por fila (por empresa) | Mover `signature_vault_id`/`identity_validation_ref` al **nivel persona** (una sola). Encaja con D4 (baúl por persona). |
| Crear una vez + anidar empresas/escrituras | form de 1 empresa; escrituras en otra pestaña | UI representante-céntrica: crear la persona y agregar empresas; por cada empresa, gestionar sus escrituras (tabla anidada). |
| Empresa con varias escrituras (historial) | Ya soportado (deeds + M:N + vigencias) | Colgar las escrituras del representante vía sus empresas (reusa deeds M:N; sin tabla nueva de escrituras). |
| Identidad vigente / firma baúl / opción de crear | Ya existe (`SignatureResolver` + banner 2 acciones) | Adaptar a nivel persona; conservar el flujo de opción (validar identidad **o** agregar firma al baúl, luego relacionar). |
| Mismo menú | 3 pestañas | D1: vista representante-céntrica que anida escrituras; baúl accesible desde ahí. |

### Bloque 3 — Flujo de trámites
| Requisito | Brecha | Cambio |
|---|---|---|
| Documentos después de actores | documentos = paso 2 (antes) | Reordenar `WizardStateQuery.StepKey` (traspaso y matrícula): actores → **documentos** → resto. Revisar gates dependientes del orden. |
| Apalancar empresa/representante | Ya cumple (para 1) | — |
| Select de múltiples representantes | lookup devuelve uno | Lookup devuelve **lista** de representantes por NIT; `<select>` en `ActorsForm`; el elegido se guarda en el actor. |
| El seleccionado firma (baúl/identidad) | firma por NIT (cualquiera) | Resolver firma por **documento del representante seleccionado** (baúl por persona / identidad por persona). |
| Escritura más próxima a vencer | `OrderByDescending(VigenciaHasta)` | D2: `OrderBy(VigenciaHasta)` **ascendente** entre vigentes (la que vence antes). Actualizar ADR-0033. |
| Persistir escritura usada post-entrega | Solo adjunto de bytes | Agregar `deed_id` usado (por actor o en `ProcedureInstance`), congelado tras entrega. |

---

## 3. Propuesta de HUs (Feature nuevo)

Ordenadas por dependencia. Capa entre corchetes. SP tentativos (Fibonacci).

**Fase A — Baúl (base de firma por persona)**
- **HU-A1 [BACKEND] (3)** Deprecar NIT + `codigo_hash` en `signature_vault`: DDL (nullable + reindex unicidad/consumo por documento), entidad, readers `FindActiveByDocumentAsync`, endpoints.
- **HU-A2 [FRONTEND] (2)** Form del baúl: quitar NIT, agregar input hash, ajustar unicidad/labels y tests.

**Fase B — Representante multi-empresa (núcleo)**
- **HU-B1 [BACKEND] (5)** Representante = persona única + puente `legal_representative_companies`; firma/identidad a nivel persona; **migración idempotente** de filas actuales.
- **HU-B2 [BACKEND] (3)** Escrituras del representante vía sus empresas (reusa deeds M:N); ajustar readers de consumo.
- **HU-B3 [FRONTEND] (5)** Vista representante-céntrica: crear una vez, anidar empresas y escrituras; baúl accesible; unificar navegación.

**Fase C — Trámites**
- **HU-C1 [BACKEND] (3)** Reordenar pasos: documentos después de actores (traspaso y matrícula) + gates.
- **HU-C2 [BACKEND] (3)** Escritura "más próxima a vencer" (`OrderBy` asc) + persistir `deed_id` usado post-entrega + **ADR-0033 actualizado**.
- **HU-C3 [FULLSTACK] (5)** Select de múltiples representantes en actores + firma con el seleccionado (baúl/identidad por su documento).

**Total tentativo:** ~29 SP.

---

## 4. Migración de datos (HU-B1)

Estado actual: N filas `company_legal_representatives`, una por (empresa, documento). Objetivo: 1 representante por (tenant, documento) + N puentes a empresas.

- Crear puente `legal_representative_companies`.
- Para cada `(tenant_id, document_number)`: elegir una fila "maestra" (p.ej. la más reciente), insertar puentes por cada `represented_company_id` distinto, mover `signature_vault_id`/`identity_validation_ref` a la maestra, migrar el puente de `procedure_types` unificado, desactivar/borrar las filas duplicadas.
- Idempotente (DDL `IF NOT EXISTS` + `ON CONFLICT DO NOTHING`), reversible por backup.
- Verificación: conteo de personas únicas antes/después y que ninguna empresa pierda su representante.

> Como #10899 se mergeó recientemente, el volumen productivo debería ser bajo; validar en `flit_local`/`flit_dev` antes de PDN.

---

## 5. Riesgos

1. **Migración de representante** (HU-B1): colapsar N filas→1 persona con puentes y mover firma/identidad. Mitigación: script idempotente + verificación + backup.
2. **Reversión ADR-0033** (D2/HU-C2): cambia una decisión documentada (mayor → menor vigencia). Mitigación: actualizar ADR y su plan #10926; test de selección explícito.
3. **Baúl sin NIT** (D4): propaga a 3 puntos de consumo — `SignatureVaultPolicy`, `LegalRepresentativeSignatureResolver`, lookup del wizard. Mitigación: refactor coordinado + tests de cada punto.
4. **Orden de pasos** (HU-C1): gates que asumían documentos antes de actores. Mitigación: revisar `WizardStateQuery` y pruebas del wizard (server-driven).

---

## 6. Notas de proceso (FLIT)

- Feature + HUs se crean **New / Sprint siguiente al activo**, sin activar (gate humano de activación).
- ADR-0033 se actualiza en estado `Propuesto`; `Aceptado` es exclusivo del Líder Técnico.
- PRs a `develop`, ≤800 líneas, con evidencias `dev-tester` por HU.
- Tras implementar cada HU: tests unitarios + evidencias en `Custom.Evidences`.
