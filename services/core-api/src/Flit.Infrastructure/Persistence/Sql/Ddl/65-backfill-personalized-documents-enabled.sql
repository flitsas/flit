-- ─────────────────────────────────────────────────────────────────────────────
-- HU #11357 (Feature #11348) — BACKFILL 1 de 2: traslada la elegibilidad de
-- documentos personalizados desde notification_channel al campo propio que crea
-- el DDL 64.
--
-- QUÉ HACE:
--   Todo tenant cuya política operativa esté hoy en notification_channel =
--   'tenant_api' queda con personalized_documents_enabled = true. Nadie más.
--
-- POR QUÉ:
--   Hasta esta HU, el ÚNICO consumidor real de notification_channel era
--   PersonalizedDocumentChannelGuard: 'tenant_api' era, de facto, el interruptor
--   de documentos personalizados (ningún correo lo consultaba — Bug #11311). Si
--   el guard deja de leer el canal sin haber trasladado antes esa elegibilidad,
--   el tenant que hoy la tiene PIERDE sus documentos personalizados en silencio.
--   Este traslado es lo que hace que el cambio de la HU #11362 sea neutro.
--
-- ORDEN: después del DDL 64 (la columna debe existir) y ANTES del backfill 66
-- (que normaliza el canal a 'flit_smtp' y borra la señal de la que se lee aquí).
-- El orden lo garantiza el timestamp de las migraciones EF gemelas, no el número
-- del archivo.
--
-- IDEMPOTENTE: es un UPDATE con condición sobre el estado destino. Re-ejecutarlo
-- no toca ninguna fila y no puede degradar a false lo que un administrador haya
-- encendido a mano después.
--
-- NO APAGA A NADIE: la condición es solo de encendido. Un tenant en 'flit_smtp'
-- se queda en false porque nace en false (default del DDL 64), no porque este
-- script lo apague.
-- ─────────────────────────────────────────────────────────────────────────────

UPDATE admin.tenant_operational_policies
   SET personalized_documents_enabled = true
 WHERE notification_channel = 'tenant_api'
   AND personalized_documents_enabled = false;
