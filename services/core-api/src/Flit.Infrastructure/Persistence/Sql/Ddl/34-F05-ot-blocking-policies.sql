-- FEATURE 05 — Política de bloqueo de preflight por Organismo de Tránsito de la compañía.
--
-- Por cada criterio del preflight (SOAT, RTM, estado del vehículo, comparendos, RNMC) una compañía
-- decide, para un OT puntual, si un hallazgo negativo BLOQUEA el trámite (rojo, subsanable con
-- "aceptar riesgo") o solo ADVIERTE (amarillo, el usuario decide continuar).
--
-- Eje ORTOGONAL a admin.tenant_transit_office_consultation_restrictions (33-F05): aquella decide SI
-- la consulta se ejecuta; esta decide, para una que SÍ corre, si su resultado bloquea. Por eso es
-- una tabla aparte y con vocabulario más amplio (incluye soat/rtm/estado_vehiculo, que no tienen
-- noción de "se consulta").
--
-- Tabla DISPERSA: solo existen filas para los pares (tenant, OT, criterio) que el admin tocó. El
-- endpoint de escritura es PUT idempotente y envía el ESTADO DESEADO (blocks=true/false), nunca un
-- verbo. Ausencia de fila = default DEL CRITERIO, definido en código (Trámites) para preservar el
-- comportamiento previo: soat/rtm/estado_vehiculo bloquean; fines/rnmc solo advierten. Cero filas
-- para las compañías existentes ⇒ cero cambio de comportamiento (sin backfill).
--
-- criterion es un CHECK cerrado, NO una FK a catálogo (mismo patrón que consultation_kind en 33-F05
-- y fines_query_source en 32-F02). El preflight que consume esta tabla mapea cada criterio a checks
-- concretos por código; añadir un criterio exige tocar ese código, así que un catálogo en BD daría
-- la falsa ilusión de que insertar una fila configura algo.
--
-- Idempotente: CREATE TABLE IF NOT EXISTS con constraints inline; DROP ... IF EXISTS antes de
-- policies/triggers (re-aplicación segura).

CREATE TABLE IF NOT EXISTS admin.tenant_transit_office_blocking_policies (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_tenant_transit_office_blocking_policies PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE CASCADE ON UPDATE CASCADE,
    -- RESTRICT: catálogo RUNT de 298 OT, no se borran (mismo criterio que ot_requirements).
    transit_office_id uuid NOT NULL REFERENCES catalogs.transit_offices(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    -- CHECK cerrado, no FK a catálogo (ver comentario de cabecera).
    criterion text NOT NULL,
    CONSTRAINT ck_tenant_transit_office_blocking_policies_criterion
        CHECK (criterion IN ('soat', 'rtm', 'estado_vehiculo', 'fines', 'rnmc')),
    -- Estado deseado: true = bloquea (fail→rojo), false = solo advierte (warn→amarillo).
    blocks boolean NOT NULL,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT uq_tenant_transit_office_blocking_policies
        UNIQUE (tenant_id, transit_office_id, criterion)
);

COMMENT ON COLUMN admin.tenant_transit_office_blocking_policies.criterion IS
    'Vocabulario propio de Admin (soat|rtm|estado_vehiculo|fines|rnmc) — NO confundir con RestrictedConsultationKinds (rnmc|fines, decide SI se consulta) ni con ConsultationKind (vehicle_vin|vehicle_plate|conductor). En C# vive como BlockingCriteria en Flit.Admin.Domain. Decide si un hallazgo negativo del preflight BLOQUEA o solo ADVIERTE.';

-- Índice general por tenant + OT: cubre la query caliente del preflight (¿qué overrides tiene este
-- tenant+OT?) y el listado admin por tenant. Barato porque la tabla es dispersa.
CREATE INDEX IF NOT EXISTS ix_tenant_transit_office_blocking_policies_tenant_office
  ON admin.tenant_transit_office_blocking_policies (tenant_id, transit_office_id);

-- RLS ESTRICTA (política comercial de la compañía, no config publicada por el OT). Sin lectura abierta.
ALTER TABLE admin.tenant_transit_office_blocking_policies ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON admin.tenant_transit_office_blocking_policies;
CREATE POLICY tenant_isolation ON admin.tenant_transit_office_blocking_policies
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP TRIGGER IF EXISTS tr_tenant_transit_office_blocking_policies_row_version
  ON admin.tenant_transit_office_blocking_policies;
CREATE TRIGGER tr_tenant_transit_office_blocking_policies_row_version
  BEFORE UPDATE ON admin.tenant_transit_office_blocking_policies
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_tenant_transit_office_blocking_policies_audit
  ON admin.tenant_transit_office_blocking_policies;
CREATE TRIGGER tr_tenant_transit_office_blocking_policies_audit
  AFTER INSERT OR UPDATE OR DELETE ON admin.tenant_transit_office_blocking_policies
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
