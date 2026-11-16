-- =============================================================================
-- HU #14318 (Feature #14315, Épica #14296) — backfill del canal de los trámites ya creados.
-- Migración: 20261105130000_HU14318_BackfillCanalTramite. Depende del DDL 172 (columnas channel y
-- channel_transit_office_id).
--
-- Solo toca filas con channel IS NULL y is_migrated = false. Los migrados de FLIT 1.0 (is_migrated) y los
-- de plataforma (origin distinto de ict y persona_natural) quedan en NULL: se leen como «tramites».
--   1. origin = ict (sin distinguir mayúsculas ni espacios, igual que TramiteFuente.Desde) → integracion.
--   2. origin = persona_natural con la marca del punto en field_key = 'observaciones' → pn_punto, con
--      channel_transit_office_id = transit_office_id (el organismo con el que nació el borrador del portal).
--      LIMITACIÓN: si el trámite ya se envió a otro OT, transit_office_id es el organismo actual y no el del
--      punto; no hay otra fuente histórica. Si la fila no tiene transit_office_id, el CHECK
--      ck_procedure_instances_channel_office rechazaría pn_punto sin OT: se deja como pn_web (paso 3).
--      La marca es la que escribe PersonaNaturalCreador.MarcaCanalPunto al inicio de la observación; los
--      borradores anteriores a esa marca (HU #14216) no son distinguibles y quedan como pn_web.
--   3. el resto de origin = persona_natural → pn_web.
--
-- Idempotente: cada UPDATE filtra channel IS NULL, así que una segunda corrida no encuentra filas.
-- Es un dato derivado, no una acción de negocio: se desactivan los triggers de usuario de la tabla mientras
-- dura (patrón de los DDL 47 y 123), para no subir row_version, no escribir auditoría por cada fila ni
-- emitir señales de sincronización/avisos (tr_procedure_instances_sync_stamp, flito sync signals). DISABLE
-- TRIGGER toma un bloqueo sobre la tabla hasta el final de la transacción de la migración; si algo falla,
-- el rollback los deja activos. RLS: el esquema tramites tiene RLS sin FORCE y la migración corre como
-- dueño de la tabla, así que ve todas las compañías.
-- Down: el backfill no se revierte (no se puede distinguir una fila asignada aquí de una creada ya con canal).
-- =============================================================================

ALTER TABLE tramites.procedure_instances DISABLE TRIGGER USER;

UPDATE tramites.procedure_instances
   SET channel = 'integracion'
 WHERE channel IS NULL
   AND is_migrated = false
   AND lower(btrim(origin)) = 'ict';

UPDATE tramites.procedure_instances p
   SET channel = 'pn_punto',
       channel_transit_office_id = p.transit_office_id
 WHERE p.channel IS NULL
   AND p.is_migrated = false
   AND p.origin = 'persona_natural'
   AND p.transit_office_id IS NOT NULL
   AND EXISTS (SELECT 1
                 FROM tramites.procedure_instance_field_values f
                WHERE f.procedure_instance_id = p.id
                  AND f.field_key = 'observaciones'
                  AND starts_with(f.value_text, '[Canal: punto de atención (OT)]'));

UPDATE tramites.procedure_instances
   SET channel = 'pn_web'
 WHERE channel IS NULL
   AND is_migrated = false
   AND origin = 'persona_natural';

ALTER TABLE tramites.procedure_instances ENABLE TRIGGER USER;
