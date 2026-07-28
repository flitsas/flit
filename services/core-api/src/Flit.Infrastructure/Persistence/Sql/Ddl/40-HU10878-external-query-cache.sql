-- ─────────────────────────────────────────────────────────────────────────────
-- HU #10878 (Feature #10862, CF-04) — Caché cross-trámite de consultas externas
-- con TTL configurable por fuente (ADR-0030) + gate mínimo de consentimiento
-- Habeas Data para el reúso de datos de persona (ADR-0031). Ver
-- docs/adr-10878-cache-consultas-ttl.md (Propuesto, database-agent materializa,
-- NO aprueba).
--
-- Idempotente: ADD COLUMN IF NOT EXISTS / CREATE TABLE (sin IF NOT EXISTS porque
-- es tabla nueva, ver Down de la migración) / UPDATE de seed sin condición de
-- reintento destructivo (vuelve a fijar el mismo valor si se re-ejecuta).
-- ─────────────────────────────────────────────────────────────────────────────

-- ============================================================================
-- 1) TTL configurable por fuente (columna nueva sobre catálogo YA EXISTENTE
--    tramites.external_data_sources, HU10151). GLOBAL por fuente, sin
--    tenant_id (excepción A20, ADR-0019, ya aplicada a esta tabla).
-- ============================================================================
ALTER TABLE tramites.external_data_sources
  ADD COLUMN IF NOT EXISTS cache_ttl_hours integer;

COMMENT ON COLUMN tramites.external_data_sources.cache_ttl_hours IS
  'Vigencia (horas) del cache de reutilizacion cross-tramite (CF-04, HU #10878). NULL = usa el default global (24h, ExternalQueryCacheRules.DefaultTtlHours en dominio).';

-- Seed inicial de TTL por fuente (ajustable sin release, vía SQL/seed como el
-- resto del catálogo HU10151).
UPDATE tramites.external_data_sources SET cache_ttl_hours = 24  WHERE code = 'RUNT';
UPDATE tramites.external_data_sources SET cache_ttl_hours = 24  WHERE code = 'SIMIT';
UPDATE tramites.external_data_sources SET cache_ttl_hours = 720 WHERE code = 'RUES';        -- 30 días: cambia poco (registro mercantil)
UPDATE tramites.external_data_sources SET cache_ttl_hours = 168 WHERE code = 'FASECOLDA';    -- 7 días
UPDATE tramites.external_data_sources SET cache_ttl_hours = 24  WHERE code = 'RNMC';
UPDATE tramites.external_data_sources SET cache_ttl_hours = 24  WHERE code = 'RESOLUCIONES';

-- ============================================================================
-- 2) tramites.external_query_cache
-- ============================================================================
CREATE TABLE tramites.external_query_cache (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_external_query_cache PRIMARY KEY (id),

    tenant_id uuid NOT NULL
        CONSTRAINT fk_external_query_cache_tenant
        REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,

    external_data_source_id uuid NOT NULL
        CONSTRAINT fk_external_query_cache_data_source
        REFERENCES tramites.external_data_sources(id) ON DELETE RESTRICT ON UPDATE CASCADE,

    subject_kind varchar(10) NOT NULL
        CONSTRAINT ck_external_query_cache_subject_kind CHECK (subject_kind IN ('person', 'vehicle')),

    -- Llave persona: tipo + número de documento.
    document_type varchar(10),
    document_number varchar(30),

    -- Llave vehículo: identificador tal como lo consulta el wizard hoy (placa O VIN, un solo campo
    -- 'plate_or_vin' en RunConsultationHandler/ConsultationTemplate). Normalizado a mayúsculas/trim
    -- por el servicio de aplicación antes de escribir.
    vehicle_identifier varchar(20),

    -- Snapshot de HydratedField[] (mismo shape que ya persiste RunConsultationHandler en field_values
    -- y que ya devuelven RuntPersonLookupHandler/RuesPersonLookupHandler vía ConsultationResult).
    payload jsonb NOT NULL DEFAULT '[]',

    queried_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL,

    source_procedure_instance_id uuid
        CONSTRAINT fk_external_query_cache_procedure_instance
        REFERENCES tramites.procedure_instances(id) ON DELETE SET NULL ON UPDATE CASCADE,

    reuse_count integer NOT NULL DEFAULT 0,
    last_reused_at timestamptz,

    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,

    CONSTRAINT ck_external_query_cache_subject_shape CHECK (
        (subject_kind = 'person'
            AND document_type IS NOT NULL AND document_number IS NOT NULL
            AND vehicle_identifier IS NULL)
        OR
        (subject_kind = 'vehicle'
            AND vehicle_identifier IS NOT NULL
            AND document_type IS NULL AND document_number IS NULL)
    ),
    CONSTRAINT ck_external_query_cache_expiry CHECK (expires_at >= queried_at)
);

-- Índices únicos parciales por sujeto (una fila reutilizable por tenant+fuente+llave).
CREATE UNIQUE INDEX uq_external_query_cache_person
  ON tramites.external_query_cache (tenant_id, external_data_source_id, document_type, document_number)
  WHERE subject_kind = 'person';

CREATE UNIQUE INDEX uq_external_query_cache_vehicle
  ON tramites.external_query_cache (tenant_id, external_data_source_id, vehicle_identifier)
  WHERE subject_kind = 'vehicle';

-- Checklist A11: tenant_id primero. Housekeeping opcional (limpieza de vencidos) usa expires_at.
CREATE INDEX ix_external_query_cache_tenant_expires
  ON tramites.external_query_cache (tenant_id, expires_at);

-- Checklist A9: índice cubriendo la FK.
CREATE INDEX ix_external_query_cache_data_source
  ON tramites.external_query_cache (external_data_source_id);

CREATE INDEX ix_external_query_cache_source_instance
  ON tramites.external_query_cache (source_procedure_instance_id);

COMMENT ON COLUMN tramites.external_query_cache.document_number IS '@pii:high';
COMMENT ON COLUMN tramites.external_query_cache.document_type IS '@pii:low';
COMMENT ON COLUMN tramites.external_query_cache.payload IS '@pii:medium — snapshot HydratedField[] de la última consulta externa (persona o vehículo).';

-- RLS (checklist A10) — mismo patrón que el resto de tramites.*.
ALTER TABLE tramites.external_query_cache ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON tramites.external_query_cache
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

-- Triggers de negocio (checklist A16).
-- EXCEPCIÓN A6 documentada (ADR-0030): sin soft-delete — es una tabla de caché pura, sin significado
-- de negocio en "borrar una fila" (se sobrescribe por upsert en la siguiente consulta); mismo criterio
-- que admin.signature_vault (estado explícito en vez de deleted_at).
DROP TRIGGER IF EXISTS tr_external_query_cache_row_version ON tramites.external_query_cache;
CREATE TRIGGER tr_external_query_cache_row_version BEFORE UPDATE ON tramites.external_query_cache
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_external_query_cache_audit ON tramites.external_query_cache;
CREATE TRIGGER tr_external_query_cache_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.external_query_cache
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

-- ============================================================================
-- 3) tramites.person_data_consents (Habeas Data — gate de reúso de PERSONAS)
-- ============================================================================
CREATE TABLE tramites.person_data_consents (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_person_data_consents PRIMARY KEY (id),

    tenant_id uuid NOT NULL
        CONSTRAINT fk_person_data_consents_tenant
        REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,

    document_type varchar(10) NOT NULL,
    document_number varchar(30) NOT NULL,

    status varchar(10) NOT NULL DEFAULT 'unknown'
        CONSTRAINT ck_person_data_consents_status CHECK (status IN ('granted', 'revoked', 'unknown')),

    consent_version varchar(40),
    consent_source varchar(40),  -- p.ej. 'actor_capture_v1' (de dónde vino la autorización)

    granted_at timestamptz,
    revoked_at timestamptz,
    captured_ip varchar(64),
    captured_user_agent varchar(120),

    source_procedure_instance_id uuid
        CONSTRAINT fk_person_data_consents_procedure_instance
        REFERENCES tramites.procedure_instances(id) ON DELETE SET NULL ON UPDATE CASCADE,

    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,

    CONSTRAINT ck_person_data_consents_dates CHECK (
        (status = 'granted' AND granted_at IS NOT NULL)
        OR (status = 'revoked' AND revoked_at IS NOT NULL)
        OR (status = 'unknown')
    )
);

CREATE UNIQUE INDEX uq_person_data_consents_person
  ON tramites.person_data_consents (tenant_id, document_type, document_number);

-- Checklist A9: índice cubriendo la FK.
CREATE INDEX ix_person_data_consents_source_instance
  ON tramites.person_data_consents (source_procedure_instance_id);

COMMENT ON COLUMN tramites.person_data_consents.document_number IS '@pii:high';
COMMENT ON COLUMN tramites.person_data_consents.document_type IS '@pii:low';
COMMENT ON COLUMN tramites.person_data_consents.captured_ip IS '@pii:low — prueba de auditoría Habeas Data';

ALTER TABLE tramites.person_data_consents ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON tramites.person_data_consents
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

-- EXCEPCIÓN A6 documentada (ADR-0031): sin soft-delete — el estado de negocio ya vive en
-- `status` (granted/revoked/unknown); "borrar" el registro de consentimiento no tiene
-- significado propio (la revocación es un cambio de estado explícito, no un DELETE).
DROP TRIGGER IF EXISTS tr_person_data_consents_row_version ON tramites.person_data_consents;
CREATE TRIGGER tr_person_data_consents_row_version BEFORE UPDATE ON tramites.person_data_consents
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_person_data_consents_audit ON tramites.person_data_consents;
CREATE TRIGGER tr_person_data_consents_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.person_data_consents
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
