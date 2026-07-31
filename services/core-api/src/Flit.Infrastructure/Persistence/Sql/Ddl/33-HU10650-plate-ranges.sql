-- HU #10650 — Inventario de preasignación de placa | Feature #10587 (R01/R05/R06)
-- Rangos de placas asignados por un OT a una compañía, explotados en placas individuales con
-- ciclo de vida (disponible/preasignada/utilizada/bloqueada/revocada).

CREATE TABLE admin.plate_ranges (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_plate_ranges PRIMARY KEY (id),
    -- tenant_id = COMPAÑÍA dueña del rango; transit_office_id = OT que lo asignó.
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE CASCADE ON UPDATE CASCADE,
    transit_office_id uuid NOT NULL REFERENCES catalogs.transit_offices(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    prefix varchar(3) NOT NULL,
    range_from int NOT NULL,
    range_to int NOT NULL,
    editable_until timestamptz NOT NULL,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT ck_plate_ranges_prefix CHECK (prefix ~ '^[A-Z]{3}$'),
    CONSTRAINT ck_plate_ranges_bounds CHECK (range_from BETWEEN 0 AND 999 AND range_to BETWEEN 0 AND 999 AND range_from <= range_to),
    CONSTRAINT uq_plate_ranges_office_prefix_bounds UNIQUE (transit_office_id, prefix, range_from, range_to)
);
CREATE INDEX ix_plate_ranges_tenant_office ON admin.plate_ranges(tenant_id, transit_office_id);

CREATE TABLE admin.plate_range_details (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_plate_range_details PRIMARY KEY (id),
    plate_range_id uuid NOT NULL REFERENCES admin.plate_ranges(id) ON DELETE CASCADE ON UPDATE CASCADE,
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE CASCADE ON UPDATE CASCADE,
    transit_office_id uuid NOT NULL REFERENCES catalogs.transit_offices(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    plate varchar(10) NOT NULL,
    state varchar(20) NOT NULL DEFAULT 'disponible',
    procedure_instance_id uuid REFERENCES tramites.procedure_instances(id) ON DELETE SET NULL ON UPDATE CASCADE,
    reserved_at timestamptz,
    used_at timestamptz,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz,
    CONSTRAINT ck_plate_range_details_state CHECK (state IN ('disponible','preasignada','utilizada','bloqueada','revocada')),
    CONSTRAINT uq_plate_range_details_office_plate UNIQUE (transit_office_id, plate)
);
CREATE INDEX ix_plate_range_details_tenant_state ON admin.plate_range_details(tenant_id, state);
CREATE INDEX ix_plate_range_details_range ON admin.plate_range_details(plate_range_id);

-- RLS: el inventario lo escriben DOS tenants distintos — el OT (crea el rango / asigna la placa
-- en el Flujo B) y la COMPAÑÍA (reserva la placa al radicar, Flujo A). Como la escritura es
-- legítimamente cross-tenant, no se puede aislar por igualdad de tenant; la autorización se hace
-- en la capa de aplicación (flag de la compañía + allow_plate_preassign del OT + grant vigente).
-- RLS queda habilitada con políticas permisivas para poder endurecerla más adelante sin migración
-- de estructura.
ALTER TABLE admin.plate_ranges ENABLE ROW LEVEL SECURITY;
CREATE POLICY plate_ranges_read ON admin.plate_ranges FOR SELECT USING (true);
CREATE POLICY plate_ranges_write ON admin.plate_ranges FOR ALL USING (true) WITH CHECK (true);

ALTER TABLE admin.plate_range_details ENABLE ROW LEVEL SECURITY;
CREATE POLICY plate_range_details_read ON admin.plate_range_details FOR SELECT USING (true);
CREATE POLICY plate_range_details_write ON admin.plate_range_details FOR ALL USING (true) WITH CHECK (true);

DROP TRIGGER IF EXISTS tr_plate_ranges_row_version ON admin.plate_ranges;
CREATE TRIGGER tr_plate_ranges_row_version BEFORE UPDATE ON admin.plate_ranges
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_plate_ranges_audit ON admin.plate_ranges;
CREATE TRIGGER tr_plate_ranges_audit AFTER INSERT OR UPDATE OR DELETE ON admin.plate_ranges
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_plate_range_details_row_version ON admin.plate_range_details;
CREATE TRIGGER tr_plate_range_details_row_version BEFORE UPDATE ON admin.plate_range_details
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_plate_range_details_audit ON admin.plate_range_details;
CREATE TRIGGER tr_plate_range_details_audit AFTER INSERT OR UPDATE OR DELETE ON admin.plate_range_details
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
