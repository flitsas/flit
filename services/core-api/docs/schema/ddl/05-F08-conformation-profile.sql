-- FEATURE-08 — Configurador Dinámico de Tipos de Trámite
-- Feature ADO: #10821 (FLIT - EVOLUTION)
-- Fase 2b: Materialización del schema del configurador dinámico.
-- Fecha: 2026-07-21 | Agente: database-agent
--
-- Dependencias previas (deben estar aplicadas):
--   04-HU10151-tramites-parametrizacion.sql
--   04-HU10151-revision-parametrizacion.sql
--   08-HU10155-documental.sql
--   06-HU10150-procedure-instances.sql
--
-- Excepciones A4/A20 documentadas en ADR-0019:
--   procedure_type_sources y procedure_document_requirements son catálogos globales
--   sin tenant_id administrados exclusivamente por el rol SuperAdmin de FLIT.
--   procedure_type_snapshots SÍ tiene tenant_id (vínculo a instancia de trámite).
--
-- ADR borrador de convergencia: §9 de docs/plan-implementacion-feature-08-configurador-dinamico.md.
-- ⚠️  PENDIENTE antes del merge: formalizar ADR-00XX (Convergencia motor dinámico / Registry
--     section_type) como archivo separado vía skill flit-adr-generator.
--
-- Ejecutado por 3 migraciones EF Core wrapper:
--   20260721100000_F08_ConformationProfile    → Parte A (ALTER tablas existentes)
--   20260721100100_F08_ProcedureTypeSources   → Parte B1 (CREATE procedure_type_sources)
--   20260721100200_F08_ProcedureTypeSnapshots → Parte B2 (CREATE procedure_type_snapshots)

-- =============================================================================
-- PARTE A — ALTER TABLE en tablas existentes
-- =============================================================================

-- ─────────────────────────────────────────────────────────────────────────────
-- A1: tramites.procedure_types — versión semántica + perfil de conformación (CFD-01)
-- ─────────────────────────────────────────────────────────────────────────────
ALTER TABLE tramites.procedure_types
    ADD COLUMN IF NOT EXISTS version integer NOT NULL DEFAULT 1,
    ADD COLUMN IF NOT EXISTS gate_profile jsonb NOT NULL DEFAULT '{}';

COMMENT ON COLUMN tramites.procedure_types.version IS
'Versión semántica del tipo. Se incrementa al publicar un cambio de configuración.
Las instancias en curso capturan esta versión en tramites.procedure_type_snapshots
para protegerse de cambios posteriores (AC#5).';

COMMENT ON COLUMN tramites.procedure_types.gate_profile IS
'Perfil de conformación dinámico del tipo de trámite.
Esquema JSON esperado:
{
  "entryMode": "PLATE" | "VIN" | "BOTH",
  "requiresSeller": bool,
  "requiresBuyer": bool,
  "allowsMultipleBuyer": bool,
  "allowsMultipleSeller": bool,
  "requiresCommercialValue": bool,
  "commercialValueSource": "FASECOLDA" | "BASE_GRAVABLE" | "MERCADO_LIBRE" | null,
  "requiresBiometrics": bool,
  "biometricActors": string[],
  "requiresSignature": bool,
  "requiresPlateRequest": bool,
  "validateCompanyRule": bool,
  "validateOtOperability": bool,
  "validateDuplicateProcedure": bool,
  "validateSoat": bool,
  "validatePazSalvoImpuesto": bool,
  "hasPrendaGate": bool,
  "simitMode": "INTERNAL" | "ONLINE" | null
}
Evaluado por DynamicGateEvaluator.cs cuando F08_DynamicProcedures flag = true.';

-- ─────────────────────────────────────────────────────────────────────────────
-- A2: tramites.procedure_sections — tipo de renderer (CFD-09, Alternativa 2)
-- ─────────────────────────────────────────────────────────────────────────────
ALTER TABLE tramites.procedure_sections
    ADD COLUMN IF NOT EXISTS section_type varchar(40) NOT NULL DEFAULT 'generic_form';

-- CHECK cerrado (catálogo estable, sincronizado con SectionRendererRegistry.tsx)
ALTER TABLE tramites.procedure_sections
    ADD CONSTRAINT ck_procedure_sections_section_type
        CHECK (section_type IN (
            'vehicle_query',
            'document_checklist',
            'actor_form',
            'commercial',
            'biometric',
            'signature_fur',
            'plate_request',
            'prenda_decision',
            'generic_form'
        ));

COMMENT ON COLUMN tramites.procedure_sections.section_type IS
'Tipo de renderer frontend. Mapea a SectionRendererRegistry en el cliente (CFD-09).
Catálogo cerrado — agregar un valor requiere PR coordinado backend + frontend.
Valores actuales:
  vehicle_query      → VehicleQuerySection (ConsultaStep)
  document_checklist → DocumentChecklistSection
  actor_form         → ActorFormSection (N actores, incl. LESSEE)
  commercial         → CommercialSection (valor venta, fuente)
  biometric          → BiometricSection
  signature_fur      → SignatureFurSection (FirmaFurStep)
  plate_request      → PlateRequestSection (FEATURE-04)
  prenda_decision    → PrendaDecisionSection (PrendaGate)
  generic_form       → GenericFormRenderer (form_fields custom)';

-- ─────────────────────────────────────────────────────────────────────────────
-- A3: tramites.procedure_document_requirements — extensión CFD-06 + A16 faltante
--
-- RECONCILIACIÓN ESTADO REAL vs PLAN §5.4:
--   • La tabla YA EXISTE (HU10155 DDL). Columnas reales:
--     id, procedure_type_id, document_type_id (uuid FK a document_types, NO code),
--     is_mandatory, default_sort_order, created_at, created_by.
--   • El plan §5.4 proponía document_type_code varchar — DESCARTADO; se usa
--     document_type_id uuid (FK normalizada, estado real del repo).
--   • Se extiende con: is_dummy, condition_group (CFD-06) + row_version,
--     updated_at, updated_by, triggers A16 que faltaban en HU10155.
-- ─────────────────────────────────────────────────────────────────────────────
ALTER TABLE tramites.procedure_document_requirements
    ADD COLUMN IF NOT EXISTS is_dummy boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS condition_group varchar(50),
    ADD COLUMN IF NOT EXISTS row_version bigint NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS updated_at timestamptz,
    ADD COLUMN IF NOT EXISTS updated_by uuid;

COMMENT ON COLUMN tramites.procedure_document_requirements.is_dummy IS
'Buzón dummy CFD-06: el documento se muestra en el checklist pero no bloquea
la validación del paso. Útil para documentos informativos o de referencia.';
COMMENT ON COLUMN tramites.procedure_document_requirements.condition_group IS
'Grupo condicional (ej: "prenda", "leasing"). El requisito solo aplica
si la condición del grupo está activa en la instancia del trámite.
NULL = siempre aplica.';

-- A16: triggers que faltaban en el DDL original HU10155 (retroadición idempotente)
DROP TRIGGER IF EXISTS tr_procedure_doc_req_row_version
    ON tramites.procedure_document_requirements;
CREATE TRIGGER tr_procedure_doc_req_row_version
    BEFORE UPDATE ON tramites.procedure_document_requirements
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_procedure_doc_req_audit
    ON tramites.procedure_document_requirements;
CREATE TRIGGER tr_procedure_doc_req_audit
    AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_document_requirements
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

-- =============================================================================
-- PARTE B — CREATE TABLE nuevas
-- =============================================================================

-- ─────────────────────────────────────────────────────────────────────────────
-- B1: tramites.procedure_type_sources — fuentes externas por tipo (CFD-04)
--
-- Catálogo global sin tenant_id: excepción A4/A20 documentada en ADR-0019.
-- PK compuesta (procedure_type_id, external_data_source_id): excepción A3
-- justificada como tabla de asociación pura; la unicidad del par ya garantiza
-- integridad sin necesidad de surrogate uuid.
-- Sin row_version (PK compuesta no permite token de concurrencia simple).
-- Sin soft-delete: is_active como flag de activación (excepción A6 en catálogos).
-- ─────────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS tramites.procedure_type_sources (
    procedure_type_id uuid NOT NULL
        REFERENCES tramites.procedure_types(id) ON DELETE CASCADE ON UPDATE CASCADE,
    external_data_source_id uuid NOT NULL
        REFERENCES tramites.external_data_sources(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    is_active boolean NOT NULL DEFAULT true,
    execution_order smallint NOT NULL DEFAULT 0,
    config jsonb NOT NULL DEFAULT '{}',
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT pk_procedure_type_sources
        PRIMARY KEY (procedure_type_id, external_data_source_id)
);

-- A9: índice en FK external_data_source_id (procedure_type_id cubierto por PK)
CREATE INDEX IF NOT EXISTS ix_procedure_type_sources_source_id
    ON tramites.procedure_type_sources(external_data_source_id);

COMMENT ON TABLE tramites.procedure_type_sources IS
'Fuentes de datos externas habilitadas por tipo de trámite (CFD-04).
Catálogo global sin tenant_id — excepción A4/A20 documentada en ADR-0019.
Solo SuperAdmin puede escribir (RBAC: tramites:catalogs:write).';
COMMENT ON COLUMN tramites.procedure_type_sources.config IS
'Configuración especial de la fuente para el tipo.
Esquema: { "simitMode": "INTERNAL" | "ONLINE", "optimizeDailyCache": bool }
Nulo efectivo = defaults del provider.';

-- Sin trigger trg_audit_log: PK compuesta (sin columna id) y
-- public.trg_audit_log() asigna NEW.id → falla en INSERT/UPDATE/DELETE.
-- Auditoría de fuentes queda cubierta por RBAC + logs de aplicación.

-- ─────────────────────────────────────────────────────────────────────────────
-- B2: tramites.procedure_type_snapshots — snapshot por instancia (CFD-01/AC#5)
--
-- Snapshot liviano capturado al crear una instancia de trámite. Protege la
-- instancia de cambios de configuración del tipo realizados posteriormente.
-- FK a procedure_instances verificada: tramites.procedure_instances(id).
-- Tiene tenant_id → RLS tenant_isolation.
-- Inmutable (INSERT-only): sin updated_at/by, sin deleted_at/by (excepción A5/A6
-- justificada por naturaleza inmutable del artefacto).
-- ─────────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS tramites.procedure_type_snapshots (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_procedure_type_snapshots PRIMARY KEY (id),
    tenant_id uuid NOT NULL
        REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    procedure_instance_id uuid NOT NULL
        REFERENCES tramites.procedure_instances(id) ON DELETE CASCADE ON UPDATE CASCADE,
    procedure_type_id uuid NOT NULL
        REFERENCES tramites.procedure_types(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    type_version integer NOT NULL,
    snapshot jsonb NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT uq_procedure_type_snapshots_instance
        UNIQUE (procedure_instance_id)
);

-- A9: índices en FKs de acceso frecuente
-- A11: tenant_id como primera columna en índice de tenant
CREATE INDEX IF NOT EXISTS ix_procedure_type_snapshots_tenant_id
    ON tramites.procedure_type_snapshots(tenant_id);
CREATE INDEX IF NOT EXISTS ix_procedure_type_snapshots_procedure_type_id
    ON tramites.procedure_type_snapshots(procedure_type_id);

COMMENT ON TABLE tramites.procedure_type_snapshots IS
'Snapshot liviano del tipo de trámite al crear una instancia (CFD-01/AC#5).
Captura: gate_profile + conformationRules + stepSectionTypes (sin form_fields).
Inmutable después del INSERT. El wizard de una instancia en curso siempre carga
el snapshot, no la versión live del tipo.';
COMMENT ON COLUMN tramites.procedure_type_snapshots.snapshot IS
'Snapshot mínimo del tipo al momento de creación de la instancia.
Esquema: {
  "code": string, "name": string, "family": string, "version": int,
  "gateProfile": { ...gate_profile fields... },
  "conformationRules": [{ "entityCode": string, "validationProfile": {...} }],
  "stepSectionTypes": [{ "stepCode": string, "sectionTypes": string[] }]
}
No incluye form_fields (los field_values de la instancia son inmutables por diseño).';

-- A10: RLS estricta por tenant_id
ALTER TABLE tramites.procedure_type_snapshots ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON tramites.procedure_type_snapshots;
CREATE POLICY tenant_isolation ON tramites.procedure_type_snapshots
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

-- A16: audit (solo INSERT, tabla inmutable — no row_version)
DROP TRIGGER IF EXISTS tr_procedure_type_snapshots_audit
    ON tramites.procedure_type_snapshots;
CREATE TRIGGER tr_procedure_type_snapshots_audit
    AFTER INSERT ON tramites.procedure_type_snapshots
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

-- =============================================================================
-- PARTE C — Feature flag F08_DynamicProcedures
-- =============================================================================
-- Sin DDL nuevo. Usa admin.ot_feature_flags (patrón existente HU10152, ver
-- 09-HU10152-ot-admin.sql). El flag es por tenant con scope configurable.
--
-- Clave: flag_key = 'F08_DynamicProcedures'
-- Default: ausencia de fila = false (gates estáticos, sin regresión).
-- Activación por tenant: INSERT INTO admin.ot_feature_flags (...) ON CONFLICT DO UPDATE.
-- El backend lee OtFeatureFlagRepository con key 'F08_DynamicProcedures' y retorna
-- false si no existe fila para el tenant (compatibilidad retroactiva total).
-- =============================================================================
