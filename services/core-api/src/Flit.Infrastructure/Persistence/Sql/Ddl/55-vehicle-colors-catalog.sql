-- Catálogo global de colores de vehículo (RUNT) — transformaciones FUR / ADR-0029.
-- Schema catalogs, sin tenant_id (checklist A20). Soft-delete vía deleted_at + is_active.

CREATE TABLE IF NOT EXISTS catalogs.vehicle_colors (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_vehicle_colors PRIMARY KEY (id),
    code varchar(20) NOT NULL,
    name varchar(120) NOT NULL,
    is_active boolean NOT NULL DEFAULT true,
    external_refs jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    deleted_at timestamptz,
    deleted_by uuid,
    CONSTRAINT uq_vehicle_colors_code UNIQUE (code)
);

CREATE INDEX IF NOT EXISTS ix_vehicle_colors_name_active
    ON catalogs.vehicle_colors (name)
    WHERE deleted_at IS NULL AND is_active = true;

CREATE INDEX IF NOT EXISTS ix_vehicle_colors_code_active
    ON catalogs.vehicle_colors (code)
    WHERE deleted_at IS NULL AND is_active = true;

COMMENT ON TABLE catalogs.vehicle_colors IS
  'Catálogo RUNT de colores de vehículo para transformaciones (cambio de color) en el wizard FUR.';
COMMENT ON COLUMN catalogs.vehicle_colors.code IS
  'Código del color en el catálogo fuente (color_code).';
COMMENT ON COLUMN catalogs.vehicle_colors.name IS
  'Descripción del color (color_description). Valor que se persiste en vehicle_color del trámite.';
COMMENT ON COLUMN catalogs.vehicle_colors.external_refs IS
  'Referencias externas JSON (p.ej. source_id del catálogo origen).';
