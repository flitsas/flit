-- Catálogo global de carrocerías de vehículo por clase RUNT.
-- Schema catalogs, sin tenant_id (checklist A20). Soft-delete vía deleted_at + is_active.
--
-- class_vehicle NULL: filas de respaldo cuando la consulta RUNT no trae clase.
-- name es el valor que el trámite persiste en vehicle_body_type (FUR texto).
--
-- DDL IDEMPOTENTE.

CREATE TABLE IF NOT EXISTS catalogs.vehicle_bodyworks (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_vehicle_bodyworks PRIMARY KEY (id),
    code varchar(20) NOT NULL,
    name varchar(120) NOT NULL,
    class_vehicle varchar(40),
    is_active boolean NOT NULL DEFAULT true,
    external_refs jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    deleted_at timestamptz,
    deleted_by uuid,
    CONSTRAINT uq_vehicle_bodyworks_code UNIQUE (code)
);

CREATE INDEX IF NOT EXISTS ix_vehicle_bodyworks_name_active
    ON catalogs.vehicle_bodyworks (name)
    WHERE deleted_at IS NULL AND is_active = true;

CREATE INDEX IF NOT EXISTS ix_vehicle_bodyworks_class_active
    ON catalogs.vehicle_bodyworks (class_vehicle)
    WHERE deleted_at IS NULL AND is_active = true;

COMMENT ON TABLE catalogs.vehicle_bodyworks IS
  'Catálogo RUNT de carrocerías por clase de vehículo para el selector de cambio de carrocería.';
COMMENT ON COLUMN catalogs.vehicle_bodyworks.code IS
  'Código de carrocería en el catálogo fuente.';
COMMENT ON COLUMN catalogs.vehicle_bodyworks.name IS
  'Descripción. Valor que se persiste en vehicle_body_type del trámite (FUR).';
COMMENT ON COLUMN catalogs.vehicle_bodyworks.class_vehicle IS
  'Clase RUNT (AUTOMOVIL, CAMION, …). NULL = respaldo si el vehículo no trae clase.';
COMMENT ON COLUMN catalogs.vehicle_bodyworks.external_refs IS
  'Referencias externas JSON.';
