-- ─────────────────────────────────────────────────────────────────────────────
-- HU #11302 (Feature #11301) — Certificaciones externas en modelo canónico
-- persistido. Materializa ADR-0041 (Propuesto; database-agent materializa, NO
-- aprueba). Ver docs/plan-fix-definitivo-tablas-certificadoras.md §4.
--
-- POR QUÉ TABLAS PROPIAS Y NO MÁS LLAVES EN procedure_instance_field_values:
--   1. field_values es INMUTABLE fuera de borrador (tr_..._immutable). Un mapeo
--      equivocado no se puede reparar jamás: ni backfill, ni reproceso, solo
--      reconsultar — que se cobra. Ese bloqueo es lo que obligó a que el
--      generador del PDF consultara el RUES EN VIVO en cada regeneración.
--   2. Una llave = un valor. El RUNT entrega el HISTÓRICO de pólizas y de
--      revisiones (hay vehículos con cuatro RTM); en field_values no cabe.
--   3. varchar libre sin tipo: cada mapper inventa formato de fecha y
--      vocabulario de estado, y nadie puede validar el conjunto.
--
-- El congelamiento pasa a ser EXPLÍCITO (frozen_at, fijado al radicar) en vez de
-- heredado del trigger. Es la decisión central del ADR: permite completar y
-- reparar mientras el trámite lo admita, que es justo lo que hoy es imposible.
--
-- Idempotente por construcción (tablas nuevas; el Down de la migración las
-- elimina). Excepciones al checklist de schema, documentadas al pie de cada
-- tabla: A11 (los índices únicos naturales no llevan tenant_id primero) y A6
-- (sin soft-delete).
-- ─────────────────────────────────────────────────────────────────────────────

-- ============================================================================
-- 1) tramites.external_query_payloads — respuesta cruda SANITIZADA
-- ============================================================================
-- Sin esto no hay forma de saber qué mandó realmente el proveedor. Es lo que
-- convierte "el DTO no modela el campo" en un defecto reparable en vez de en un
-- dato perdido: se corrige el mapper y se reprocesa sin volver a pagar.
CREATE TABLE tramites.external_query_payloads (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_external_query_payloads PRIMARY KEY (id),

    tenant_id uuid NOT NULL
        CONSTRAINT fk_external_query_payloads_tenant
        REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,

    procedure_instance_id uuid NOT NULL
        CONSTRAINT fk_external_query_payloads_procedure_instance
        REFERENCES tramites.procedure_instances(id) ON DELETE CASCADE ON UPDATE CASCADE,

    provider_key varchar(40) NOT NULL,

    subject_kind varchar(10) NOT NULL
        CONSTRAINT ck_external_query_payloads_subject_kind
        CHECK (subject_kind IN ('vehicle', 'company', 'person')),

    -- Placa/VIN o NIT. El sujeto, no el dato: sirve para localizar el payload al
    -- reprocesar sin tener que abrirlo.
    subject_key varchar(40),

    payload jsonb NOT NULL,

    queried_at timestamptz NOT NULL,

    -- D6 — RETENCIÓN INDEFINIDA por decisión del PO (2026-08-07): nullable y sin
    -- job de purga. La columna se conserva para poder acotar el plazo después
    -- sin migración; hoy nadie la escribe.
    expires_at timestamptz,

    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,

    CONSTRAINT ck_external_query_payloads_expiry CHECK (expires_at IS NULL OR expires_at >= queried_at)
);

CREATE INDEX ix_external_query_payloads_tenant_instance
  ON tramites.external_query_payloads (tenant_id, procedure_instance_id, queried_at DESC);

-- Checklist A9: índice cubriendo la FK.
CREATE INDEX ix_external_query_payloads_instance
  ON tramites.external_query_payloads (procedure_instance_id);

-- Reproceso dirigido: "todos los payloads de kyverum_runt de vehículo".
CREATE INDEX ix_external_query_payloads_provider_subject
  ON tramites.external_query_payloads (provider_key, subject_kind, queried_at DESC);

COMMENT ON TABLE tramites.external_query_payloads IS
  'Respuesta cruda sanitizada de una consulta externa (HU #11302, ADR-0041). Habilita reprocesar un mapeo corregido sin volver a pagar la consulta.';
COMMENT ON COLUMN tramites.external_query_payloads.payload IS
  '@pii:high — respuesta del proveedor. El payload del RUES incluye nombres y documentos de representantes legales dentro del texto de facultades. Sanitizado antes de escribir. PROHIBIDO volcarlo en trazas, logs, PRs o comentarios de ADO (Ley 1581).';
COMMENT ON COLUMN tramites.external_query_payloads.subject_key IS '@pii:low — placa/VIN o NIT consultado.';
COMMENT ON COLUMN tramites.external_query_payloads.expires_at IS
  'D6: retención indefinida por decisión del PO (2026-08-07). NULL = sin plazo. Se conserva la columna para acotar el plazo sin migración.';

ALTER TABLE tramites.external_query_payloads ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON tramites.external_query_payloads
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

-- EXCEPCIÓN A6 documentada: sin soft-delete. Un payload es evidencia de auditoría;
-- "borrarlo a medias" no tiene significado de negocio. Si se acota la retención,
-- la purga es un DELETE real y el rastro queda en la bitácora del trigger.
DROP TRIGGER IF EXISTS tr_external_query_payloads_row_version ON tramites.external_query_payloads;
CREATE TRIGGER tr_external_query_payloads_row_version BEFORE UPDATE ON tramites.external_query_payloads
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_external_query_payloads_audit ON tramites.external_query_payloads;
CREATE TRIGGER tr_external_query_payloads_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.external_query_payloads
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

-- ============================================================================
-- 2) tramites.vehicle_soat_policies — histórico de pólizas de SOAT
-- ============================================================================
-- Cada columna de dato lleva su PAR canónico + crudo. Es la regla transversal
-- del modelo: lo que no se puede interpretar no se inventa ni se descarta —
-- queda el crudo, el canónico en NULL, y el campo listado en
-- normalization_issues (que es la lista de trabajo para arreglar el mapper).
CREATE TABLE tramites.vehicle_soat_policies (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_vehicle_soat_policies PRIMARY KEY (id),

    tenant_id uuid NOT NULL
        CONSTRAINT fk_vehicle_soat_policies_tenant
        REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,

    procedure_instance_id uuid NOT NULL
        CONSTRAINT fk_vehicle_soat_policies_procedure_instance
        REFERENCES tramites.procedure_instances(id) ON DELETE CASCADE ON UPDATE CASCADE,

    -- Identidad de la póliza dentro del trámite: número + vencimiento. Es lo que
    -- hace que reconsultar ACTUALICE la fila en vez de duplicarla.
    natural_key varchar(140) NOT NULL,

    -- numSoat llega con 16 dígitos (por encima de int) y a veces con ceros a la
    -- izquierda que forman parte del número impreso. Es texto, no número.
    policy_number varchar(60),
    policy_number_raw text,

    insurer_name varchar(400),
    insurer_name_raw text,

    -- date y no timestamptz: un certificado imprime un DÍA. Arrastrar hora+zona
    -- es lo que permite que una normalización a UTC corra el día impreso.
    issued_on date,
    issued_on_raw text,

    valid_from date,
    valid_from_raw text,

    valid_until date,
    valid_until_raw text,

    vigency_status varchar(12) NOT NULL DEFAULT 'unknown'
        CONSTRAINT ck_vehicle_soat_policies_vigency
        CHECK (vigency_status IN ('vigente', 'vencido', 'no_aplica', 'unknown')),
    vigency_status_raw text,

    -- La que va al certificado (D9: solo la vigente). Índice único parcial abajo.
    is_current boolean NOT NULL DEFAULT false,

    source_kind varchar(12) NOT NULL
        CONSTRAINT ck_vehicle_soat_policies_source_kind
        CHECK (source_kind IN ('consultation', 'user', 'ocr', 'system')),
    provider_key varchar(40) NOT NULL,
    observed_at timestamptz NOT NULL,
    raw_payload_id uuid
        CONSTRAINT fk_vehicle_soat_policies_raw_payload
        REFERENCES tramites.external_query_payloads(id) ON DELETE SET NULL ON UPDATE CASCADE,
    mapper_version varchar(20) NOT NULL DEFAULT 'unknown',
    normalization_issues jsonb NOT NULL DEFAULT '[]',

    -- Congelamiento EXPLÍCITO al radicar, en vez del trigger de inmutabilidad de
    -- field_values. La diferencia importa: mientras es NULL el dato se puede
    -- completar y corregir; una vez fijado, el expediente queda estable.
    frozen_at timestamptz,

    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid
);

-- EXCEPCIÓN A11 documentada (ADR-0041): la unicidad natural es POR INSTANCIA, no
-- por tenant — procedure_instance_id ya determina el tenant. Anteponer tenant_id
-- haría el índice inútil para el upsert, que llega con la instancia en la mano.
CREATE UNIQUE INDEX uq_vehicle_soat_policies_instance_natural
  ON tramites.vehicle_soat_policies (procedure_instance_id, natural_key);

-- Una sola póliza vigente por trámite: la que imprime el certificado.
CREATE UNIQUE INDEX uq_vehicle_soat_policies_current
  ON tramites.vehicle_soat_policies (procedure_instance_id)
  WHERE is_current;

CREATE INDEX ix_vehicle_soat_policies_tenant_instance
  ON tramites.vehicle_soat_policies (tenant_id, procedure_instance_id);

CREATE INDEX ix_vehicle_soat_policies_raw_payload
  ON tramites.vehicle_soat_policies (raw_payload_id);

-- Reproceso por versión de mapeo: "qué filas produjo el mapper viejo".
CREATE INDEX ix_vehicle_soat_policies_mapper_version
  ON tramites.vehicle_soat_policies (provider_key, mapper_version);

COMMENT ON TABLE tramites.vehicle_soat_policies IS
  'Histórico de pólizas de SOAT certificadas por una fuente externa (HU #11302, ADR-0041). Cada dato lleva canónico + crudo + procedencia.';
COMMENT ON COLUMN tramites.vehicle_soat_policies.natural_key IS
  'numero|vencimiento. Identidad de la póliza dentro del trámite: hace idempotente la reconsulta.';
COMMENT ON COLUMN tramites.vehicle_soat_policies.normalization_issues IS
  'Campos que llegaron del proveedor y no se pudieron normalizar (crudo presente, canónico NULL). Lista de trabajo para corregir el mapper sin volver a consultar.';
COMMENT ON COLUMN tramites.vehicle_soat_policies.frozen_at IS
  'Congelamiento explícito al radicar. NULL = el dato aún se puede completar o corregir.';

ALTER TABLE tramites.vehicle_soat_policies ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON tramites.vehicle_soat_policies
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

-- EXCEPCIÓN A6 documentada: sin soft-delete. Una certificación no se "da de baja":
-- o la reemplaza otra observación (upsert por natural_key) o deja de ser is_current.
DROP TRIGGER IF EXISTS tr_vehicle_soat_policies_row_version ON tramites.vehicle_soat_policies;
CREATE TRIGGER tr_vehicle_soat_policies_row_version BEFORE UPDATE ON tramites.vehicle_soat_policies
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_vehicle_soat_policies_audit ON tramites.vehicle_soat_policies;
CREATE TRIGGER tr_vehicle_soat_policies_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.vehicle_soat_policies
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

-- ============================================================================
-- 3) tramites.vehicle_rtm_inspections — histórico de revisiones técnico-mecánicas
-- ============================================================================
CREATE TABLE tramites.vehicle_rtm_inspections (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_vehicle_rtm_inspections PRIMARY KEY (id),

    tenant_id uuid NOT NULL
        CONSTRAINT fk_vehicle_rtm_inspections_tenant
        REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,

    procedure_instance_id uuid NOT NULL
        CONSTRAINT fk_vehicle_rtm_inspections_procedure_instance
        REFERENCES tramites.procedure_instances(id) ON DELETE CASCADE ON UPDATE CASCADE,

    natural_key varchar(140) NOT NULL,

    certificate_number varchar(60),
    certificate_number_raw text,

    cda_name varchar(400),
    cda_name_raw text,

    issued_on date,
    issued_on_raw text,

    valid_from date,
    valid_from_raw text,

    valid_until date,
    valid_until_raw text,

    -- OJO: 'APROBADA' NO es vigencia. Es el resultado del trámite de la revisión.
    -- Hay vehículos con cuatro revisiones APROBADA y ninguna vigente (YNK04A).
    -- Se normaliza a 'unknown' y la selección de la vigente va por FECHA.
    vigency_status varchar(12) NOT NULL DEFAULT 'unknown'
        CONSTRAINT ck_vehicle_rtm_inspections_vigency
        CHECK (vigency_status IN ('vigente', 'vencido', 'no_aplica', 'unknown')),
    vigency_status_raw text,

    -- El RUNT lo manda, no va en el certificado, y al auditar distingue una
    -- revisión de particular de una de servicio público.
    inspection_type varchar(60),

    is_current boolean NOT NULL DEFAULT false,

    source_kind varchar(12) NOT NULL
        CONSTRAINT ck_vehicle_rtm_inspections_source_kind
        CHECK (source_kind IN ('consultation', 'user', 'ocr', 'system')),
    provider_key varchar(40) NOT NULL,
    observed_at timestamptz NOT NULL,
    raw_payload_id uuid
        CONSTRAINT fk_vehicle_rtm_inspections_raw_payload
        REFERENCES tramites.external_query_payloads(id) ON DELETE SET NULL ON UPDATE CASCADE,
    mapper_version varchar(20) NOT NULL DEFAULT 'unknown',
    normalization_issues jsonb NOT NULL DEFAULT '[]',

    frozen_at timestamptz,

    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid
);

-- EXCEPCIÓN A11 documentada: ver vehicle_soat_policies.
CREATE UNIQUE INDEX uq_vehicle_rtm_inspections_instance_natural
  ON tramites.vehicle_rtm_inspections (procedure_instance_id, natural_key);

CREATE UNIQUE INDEX uq_vehicle_rtm_inspections_current
  ON tramites.vehicle_rtm_inspections (procedure_instance_id)
  WHERE is_current;

CREATE INDEX ix_vehicle_rtm_inspections_tenant_instance
  ON tramites.vehicle_rtm_inspections (tenant_id, procedure_instance_id);

CREATE INDEX ix_vehicle_rtm_inspections_raw_payload
  ON tramites.vehicle_rtm_inspections (raw_payload_id);

CREATE INDEX ix_vehicle_rtm_inspections_mapper_version
  ON tramites.vehicle_rtm_inspections (provider_key, mapper_version);

COMMENT ON TABLE tramites.vehicle_rtm_inspections IS
  'Histórico de revisiones técnico-mecánicas certificadas (HU #11302, ADR-0041). vigency_status NUNCA se deriva del texto APROBADA.';
COMMENT ON COLUMN tramites.vehicle_rtm_inspections.vigency_status IS
  'Vocabulario cerrado. APROBADA se normaliza a unknown: describe el resultado del trámite de la revisión, no su vigencia.';

ALTER TABLE tramites.vehicle_rtm_inspections ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON tramites.vehicle_rtm_inspections
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

-- EXCEPCIÓN A6 documentada: ver vehicle_soat_policies.
DROP TRIGGER IF EXISTS tr_vehicle_rtm_inspections_row_version ON tramites.vehicle_rtm_inspections;
CREATE TRIGGER tr_vehicle_rtm_inspections_row_version BEFORE UPDATE ON tramites.vehicle_rtm_inspections
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_vehicle_rtm_inspections_audit ON tramites.vehicle_rtm_inspections;
CREATE TRIGGER tr_vehicle_rtm_inspections_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.vehicle_rtm_inspections
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

-- ============================================================================
-- 4) tramites.company_registrations — registro mercantil (RUES) por NIT
-- ============================================================================
-- Sustituye al snapshot congelado en la llave rues_snapshots_json. La diferencia
-- no es de formato: aquel vivía en field_values (inmutable fuera de borrador), y
-- por eso una compañía sin snapshot solo podía conseguirlo consultando EN VIVO al
-- generar el PDF. Con esta tabla, generar el expediente cuesta cero llamadas.
CREATE TABLE tramites.company_registrations (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_company_registrations PRIMARY KEY (id),

    tenant_id uuid NOT NULL
        CONSTRAINT fk_company_registrations_tenant
        REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,

    procedure_instance_id uuid NOT NULL
        CONSTRAINT fk_company_registrations_procedure_instance
        REFERENCES tramites.procedure_instances(id) ON DELETE CASCADE ON UPDATE CASCADE,

    -- Una compañía, una fila por trámite.
    nit varchar(20) NOT NULL,

    business_name varchar(400),
    business_name_raw text,

    registration_number varchar(60),
    registration_number_raw text,

    -- D5: se guarda el CRUDO y se deriva el canónico. Un estado no visto se
    -- imprime tal cual y no rompe el certificado ni bloquea el trámite.
    registration_status varchar(12) NOT NULL DEFAULT 'unknown'
        CONSTRAINT ck_company_registrations_status
        CHECK (registration_status IN ('vigente', 'vencido', 'no_aplica', 'unknown')),
    registration_status_raw text,

    registered_on date,
    registered_on_raw text,

    renewed_on date,
    renewed_on_raw text,

    chamber_of_commerce varchar(400),
    chamber_of_commerce_raw text,

    category varchar(400),
    category_raw text,

    address varchar(400),
    address_raw text,

    city varchar(400),
    city_raw text,

    -- Hoy se paga y se tira. Guardarlo cuesta cero y el certificado lo va a pedir.
    legal_representatives jsonb NOT NULL DEFAULT '[]',

    source_kind varchar(12) NOT NULL
        CONSTRAINT ck_company_registrations_source_kind
        CHECK (source_kind IN ('consultation', 'user', 'ocr', 'system')),
    provider_key varchar(40) NOT NULL,
    observed_at timestamptz NOT NULL,
    raw_payload_id uuid
        CONSTRAINT fk_company_registrations_raw_payload
        REFERENCES tramites.external_query_payloads(id) ON DELETE SET NULL ON UPDATE CASCADE,
    mapper_version varchar(20) NOT NULL DEFAULT 'unknown',
    normalization_issues jsonb NOT NULL DEFAULT '[]',

    frozen_at timestamptz,

    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,

    CONSTRAINT ck_company_registrations_nit CHECK (length(btrim(nit)) > 0)
);

-- EXCEPCIÓN A11 documentada: ver vehicle_soat_policies.
CREATE UNIQUE INDEX uq_company_registrations_instance_nit
  ON tramites.company_registrations (procedure_instance_id, nit);

CREATE INDEX ix_company_registrations_tenant_instance
  ON tramites.company_registrations (tenant_id, procedure_instance_id);

CREATE INDEX ix_company_registrations_raw_payload
  ON tramites.company_registrations (raw_payload_id);

CREATE INDEX ix_company_registrations_mapper_version
  ON tramites.company_registrations (provider_key, mapper_version);

COMMENT ON TABLE tramites.company_registrations IS
  'Registro mercantil (RUES) por trámite y NIT (HU #11302, ADR-0041). Sustituye el snapshot en la llave rues_snapshots_json y elimina la consulta en vivo al generar el PDF.';
COMMENT ON COLUMN tramites.company_registrations.nit IS '@pii:low — identificador de persona jurídica.';
COMMENT ON COLUMN tramites.company_registrations.legal_representatives IS
  '@pii:high — nombres, tipo y número de documento de los representantes legales, y el texto de facultades (que puede llevar más nombres embebidos). PROHIBIDO volcarlo en trazas, logs, PRs o comentarios de ADO (Ley 1581).';
COMMENT ON COLUMN tramites.company_registrations.registration_status IS
  'D5: canónico derivado. El texto tal como lo dijo el RUES vive en registration_status_raw y es lo que imprime el certificado cuando el canónico es unknown.';

ALTER TABLE tramites.company_registrations ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON tramites.company_registrations
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

-- EXCEPCIÓN A6 documentada: ver vehicle_soat_policies.
DROP TRIGGER IF EXISTS tr_company_registrations_row_version ON tramites.company_registrations;
CREATE TRIGGER tr_company_registrations_row_version BEFORE UPDATE ON tramites.company_registrations
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_company_registrations_audit ON tramites.company_registrations;
CREATE TRIGGER tr_company_registrations_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.company_registrations
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
