-- =============================================================================
-- HU #14317 (Feature #14315, Épica #14296) — canal del trámite y OT del canal.
-- Migración: 20261105120000_HU14317_CanalTramite.
--
-- tramites.procedure_instances.channel varchar(20) NULL
--     tramites | pn_web | pn_punto | integracion | aplicacion. NULL = sin dato (histórico).
-- tramites.procedure_instances.channel_transit_office_id uuid NULL
--     Organismo del punto de atención; solo con channel = 'pn_punto'.
--
-- Sin backfill (HU #14318): las filas existentes quedan NULL. ADD COLUMN nullable sin DEFAULT es solo
-- catálogo (no reescribe la tabla). Los CHECK y la FK se agregan NOT VALID y se validan aparte: la
-- validación toma SHARE UPDATE EXCLUSIVE y no bloquea lecturas ni escrituras.
-- Idempotente: se puede correr más de una vez.
-- =============================================================================

ALTER TABLE tramites.procedure_instances
    ADD COLUMN IF NOT EXISTS channel varchar(20) NULL,
    ADD COLUMN IF NOT EXISTS channel_transit_office_id uuid NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_procedure_instances_channel'
                   AND conrelid = 'tramites.procedure_instances'::regclass) THEN
        ALTER TABLE tramites.procedure_instances
            ADD CONSTRAINT ck_procedure_instances_channel
            CHECK (channel IS NULL OR channel IN ('tramites', 'pn_web', 'pn_punto', 'integracion', 'aplicacion')) NOT VALID;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_procedure_instances_channel_office'
                   AND conrelid = 'tramites.procedure_instances'::regclass) THEN
        ALTER TABLE tramites.procedure_instances
            ADD CONSTRAINT ck_procedure_instances_channel_office
            CHECK (COALESCE(channel = 'pn_punto', false) = (channel_transit_office_id IS NOT NULL)) NOT VALID;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_procedure_instances_channel_transit_office'
                   AND conrelid = 'tramites.procedure_instances'::regclass) THEN
        ALTER TABLE tramites.procedure_instances
            ADD CONSTRAINT fk_procedure_instances_channel_transit_office
            FOREIGN KEY (channel_transit_office_id) REFERENCES catalogs.transit_offices (id)
            ON DELETE RESTRICT ON UPDATE CASCADE NOT VALID;
    END IF;
END $$;

ALTER TABLE tramites.procedure_instances VALIDATE CONSTRAINT ck_procedure_instances_channel;
ALTER TABLE tramites.procedure_instances VALIDATE CONSTRAINT ck_procedure_instances_channel_office;
ALTER TABLE tramites.procedure_instances VALIDATE CONSTRAINT fk_procedure_instances_channel_transit_office;

-- Cubre la FK (RESTRICT al borrar un organismo) y el filtro «OT del punto». Parcial: casi todas las filas son NULL.
CREATE INDEX IF NOT EXISTS ix_procedure_instances_channel_transit_office_id
    ON tramites.procedure_instances (channel_transit_office_id)
    WHERE channel_transit_office_id IS NOT NULL;

COMMENT ON COLUMN tramites.procedure_instances.channel IS
    'Canal por el que entró el trámite (HU #14317): tramites, pn_web, pn_punto, integracion o aplicacion. NULL = sin dato (histórico; se lee como tramites). Catálogo en TramiteCanal (backend) y CHECK ck_procedure_instances_channel.';

COMMENT ON COLUMN tramites.procedure_instances.channel_transit_office_id IS
    'Organismo de tránsito (catalogs.transit_offices) del punto de atención por el que entró el trámite (HU #14317). Solo con channel = pn_punto (ck_procedure_instances_channel_office); NULL en los demás. Independiente de transit_office_id, que puede cambiar al enviar al OT.';
