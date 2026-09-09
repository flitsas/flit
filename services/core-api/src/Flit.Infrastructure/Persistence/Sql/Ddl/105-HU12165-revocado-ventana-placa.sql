-- HU #12165 (Feature #12156) — soporte de datos para el estado Revocado y la ventana de 1 hora de
-- corrección de placa por el OT. Las HUs de negocio (#12166 Revocar, #12167 Actualizar placa) escriben
-- estas columnas; aquí solo se agrega el esquema.
--
-- `status` NO tiene CHECK de enum en BD (se valida en aplicación, TramiteEstado.EsValido), así que
-- agregar 'revocado' como valor de negocio no requiere tocar ninguna restricción aquí.
--
-- plate_assigned_at / plate_updated_at: `updated_at` NO sirve de base para la ventana de 1 hora
-- porque cualquier otra escritura sobre la instancia lo pisa. Dos columnas dedicadas, escritas SOLO
-- por AssignPlateAsync/UpdatePlateAsync:
--   · plate_assigned_at: momento de la asignación original. Base del cálculo de la ventana.
--   · plate_updated_at: NULL mientras no se haya usado la única corrección permitida (HU #12167 AC3);
--     no-NULL bloquea un segundo intento aunque siga dentro de la hora.

ALTER TABLE tramites.procedure_instances
    ADD COLUMN IF NOT EXISTS plate_assigned_at timestamptz NULL,
    ADD COLUMN IF NOT EXISTS plate_updated_at timestamptz NULL;

ALTER TABLE tramites.procedure_instance_attachments
    ADD COLUMN IF NOT EXISTS is_historico boolean NOT NULL DEFAULT false;
