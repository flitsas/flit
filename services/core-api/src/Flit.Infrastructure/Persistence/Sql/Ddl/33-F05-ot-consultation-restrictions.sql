-- HU #10759 — Restricciones de consulta por Organismo de Tránsito (RNMC, comparendos) | Feature #F05
--
-- Tabla DISPERSA: solo existen filas para los pares (tenant, OT, kind) que el admin de la
-- compañía tocó explícitamente. Ausencia de fila = permisivo (comportamiento actual, sin
-- restricción). El endpoint de escritura es PUT idempotente y envía el ESTADO DESEADO
-- (enabled=true/false), nunca un verbo de alta/baja: así el trigger de auditoría captura
-- ambos sentidos del flip sin necesitar dos endpoints ni 404. Cero filas para las compañías
-- existentes ⇒ cero cambio de comportamiento (sin backfill).
--
-- consultation_kind es un CHECK cerrado, NO una FK a catálogo (mismo patrón que
-- fines_query_source, ver 32-F02-fines-query-source.sql). Añadir un nuevo "kind" exige tocar
-- código: el fan-out del preflight que consume esta tabla está hardcodeado por kind, así que
-- un catálogo en BD daría la falsa ilusión de que insertar una fila habilita algo.
--
-- 'vehicle' queda DELIBERADAMENTE FUERA del CHECK: restringir la consulta de vehículo
-- rompería la hidratación de field_values y la generación del FUR, que dependen de esa
-- consulta para poblar datos obligatorios del trámite. Solo 'rnmc' y 'fines' son
-- restringibles hoy (ver COMMENT ON COLUMN más abajo sobre la colisión de vocabulario).
--
-- Idempotente: CREATE TABLE IF NOT EXISTS con constraints inline; DROP ... IF EXISTS antes
-- de policies/triggers (re-aplicación segura).

CREATE TABLE IF NOT EXISTS admin.tenant_transit_office_consultation_restrictions (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_tenant_transit_office_consultation_restrictions PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE CASCADE ON UPDATE CASCADE,
    -- RESTRICT: catálogo RUNT de 298 OT, no se borran (mismo criterio que ot_requirements,
    -- ver 23-HU10545-ot-requirements.sql).
    transit_office_id uuid NOT NULL REFERENCES catalogs.transit_offices(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    -- CHECK cerrado, no FK a catálogo (ver comentario de cabecera).
    consultation_kind text NOT NULL,
    CONSTRAINT ck_tenant_transit_office_consultation_restrictions_kind
        CHECK (consultation_kind IN ('rnmc', 'fines')),
    -- Estado deseado (no verbo): true = permitida (default, sin restricción), false = inhabilitada.
    enabled boolean NOT NULL DEFAULT true,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT uq_tenant_transit_office_consultation_restrictions
        UNIQUE (tenant_id, transit_office_id, consultation_kind)
);

COMMENT ON COLUMN admin.tenant_transit_office_consultation_restrictions.consultation_kind IS
    'Vocabulario propio de Admin (rnmc|fines) — NO confundir con Flit.Tramites.Application.UseCases.Consultations.ConsultationKind (vehicle_vin|vehicle_plate|conductor), que enumera el TIPO de dato consultado, no una política de restricción comercial. En C# vive como RestrictedConsultationKinds en Flit.Admin.Domain. "vehicle" está deliberadamente fuera del CHECK: restringirlo rompería la hidratación de field_values y el FUR.';

-- Índice general por tenant (FK identity.tenants + primera columna en consultas admin).
CREATE INDEX IF NOT EXISTS ix_tenant_transit_office_consultation_restrictions_tenant_id
  ON admin.tenant_transit_office_consultation_restrictions (tenant_id);

-- Índice PARCIAL: cubre la query caliente del preflight (¿qué kinds están inhabilitados para
-- este tenant+OT?). Barato porque la tabla es dispersa (solo filas tocadas por el admin).
CREATE INDEX IF NOT EXISTS ix_tenant_transit_office_consultation_restrictions_disabled
  ON admin.tenant_transit_office_consultation_restrictions (tenant_id, transit_office_id)
  WHERE enabled = false;

-- RLS ESTRICTA (como grants, NO como ot_requirements): es política comercial de la compañía,
-- no config publicada por el OT. Sin lectura abierta.
ALTER TABLE admin.tenant_transit_office_consultation_restrictions ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON admin.tenant_transit_office_consultation_restrictions;
CREATE POLICY tenant_isolation ON admin.tenant_transit_office_consultation_restrictions
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP TRIGGER IF EXISTS tr_tenant_transit_office_consultation_restrictions_row_version
  ON admin.tenant_transit_office_consultation_restrictions;
CREATE TRIGGER tr_tenant_transit_office_consultation_restrictions_row_version
  BEFORE UPDATE ON admin.tenant_transit_office_consultation_restrictions
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_tenant_transit_office_consultation_restrictions_audit
  ON admin.tenant_transit_office_consultation_restrictions;
CREATE TRIGGER tr_tenant_transit_office_consultation_restrictions_audit
  AFTER INSERT OR UPDATE OR DELETE ON admin.tenant_transit_office_consultation_restrictions
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
