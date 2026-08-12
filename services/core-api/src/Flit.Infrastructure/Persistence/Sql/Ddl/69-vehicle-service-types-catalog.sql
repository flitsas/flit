-- Catálogo global de tipos de servicio del vehículo (sección 18 del FUR) — ADR-0019.
-- Schema catalogs, sin tenant_id (checklist A20). Soft-delete vía deleted_at + is_active.
--
-- ── Por qué global y sin tenant_id ───────────────────────────────────────────────
-- Es el mismo criterio que catalogs.vehicle_colors y catalogs.rejection_reasons: los 6 tipos de
-- servicio del vehículo (particular, público, diplomático, oficial, especial, otros) son un cierre
-- normativo del formato único de registro (FUR), no una decisión de cada organismo de tránsito. Si
-- cada tenant pudiera redefinirlos se rompería la trazabilidad con el resto del RUNT.
--
-- ── Por qué `code` es contrato y no se debe alterar ──────────────────────────────
-- Los 6 códigos (PARTICULAR, PUBLICO, DIPLOMATICO, OFICIAL, ESPECIAL, OTROS) los consume
-- `FurFieldMapper.MarkServicio` para marcar la casilla `vehicle_service_type_N` correcta en el
-- overlay del FUR. `sort_order` (1-6) es, a propósito, el mismo número N de esa casilla: no es un
-- orden de presentación arbitrario, es el orden impreso en el formato oficial.
-- DDL IDEMPOTENTE.
CREATE TABLE IF NOT EXISTS catalogs.vehicle_service_types (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_vehicle_service_types PRIMARY KEY (id),
    code varchar(20) NOT NULL,
    name varchar(120) NOT NULL,
    -- Orden normativo de las casillas 1-6 de la sección 18 del FUR. No es cosmético: lo consume
    -- FurFieldMapper indirectamente vía el código, y el listado del selector lo respeta.
    sort_order integer NOT NULL,
    is_active boolean NOT NULL DEFAULT true,
    external_refs jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    deleted_at timestamptz,
    deleted_by uuid,
    CONSTRAINT uq_vehicle_service_types_code UNIQUE (code)
);

CREATE INDEX IF NOT EXISTS ix_vehicle_service_types_active_sort
    ON catalogs.vehicle_service_types (sort_order)
    WHERE deleted_at IS NULL AND is_active = true;

COMMENT ON TABLE catalogs.vehicle_service_types IS
  'Catálogo global (ADR-0019) de tipos de servicio del vehículo — sección 18 del FUR. Códigos '
  'cerrados y contrato con FurFieldMapper: PARTICULAR, PUBLICO, DIPLOMATICO, OFICIAL, ESPECIAL, OTROS.';
COMMENT ON COLUMN catalogs.vehicle_service_types.code IS
  'Código estable, contrato con FurFieldMapper.MarkServicio (marca vehicle_service_type_<sort_order> en el overlay del FUR). No modificar sin coordinar con Documents/Fur.';
COMMENT ON COLUMN catalogs.vehicle_service_types.name IS
  'Nombre visible del tipo de servicio (p.ej. "Particular").';
COMMENT ON COLUMN catalogs.vehicle_service_types.sort_order IS
  'Orden normativo 1-6 de las casillas de la sección 18 del FUR. No es un orden de presentación libre.';
COMMENT ON COLUMN catalogs.vehicle_service_types.external_refs IS
  'Referencias externas JSON (reservado; el catálogo es cerrado y no tiene fuente externa hoy).';
