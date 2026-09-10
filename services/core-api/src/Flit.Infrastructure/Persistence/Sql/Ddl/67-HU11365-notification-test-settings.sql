-- ─────────────────────────────────────────────────────────────────────────────
-- HU #11365 (Feature #11349) — Banco de pruebas de notificaciones: configuración
-- de fila única (buzón de pruebas + marca del último envío de prueba).
--
-- SOLO ESQUEMA. Aquí no hay endpoints (HU #11366) ni envío de prueba ni cálculo
-- del límite de frecuencia (HU #11368). Esta migración solo crea la tabla, la
-- siembra con su única fila y deja los dos campos en NULL.
--
-- POR QUÉ EN BD Y NO EN MEMORIA DE PROCESO:
--   La marca del último envío sostiene el límite de frecuencia de la HU #11368.
--   Un contador en memoria se reinicia con el proceso y no lo comparten las
--   réplicas: el límite se saltaría reiniciando el contenedor o pegándole a otra
--   instancia. Persistido, el límite es una propiedad del sistema, no de un pod.
--
-- SIN tenant_id — Y POR TANTO SIN RLS (deliberado, no un olvido):
--   El buzón de pruebas es GLOBAL DE PLATAFORMA: lo gestiona el SuperAdmin y
--   sirve para probar cómo se ven las plantillas, no para operar trámites de un
--   cliente. Nadie lo consulta «en nombre de» un tenant, así que no hay
--   pertenencia que aislar. Es el mismo razonamiento que ya documenta
--   admin.quipux_settings (32-HU10710-quipux-integracion.sql): «Sin tenant_id:
--   el contrato con Quipux es de FLIT como consumidor, no de cada cliente. Por
--   eso tampoco lleva RLS». Una tabla en `admin` sin RLS llama la atención con
--   razón: quien revise esto merece saber que es una decisión, no un descuido.
--
-- SIN SOFT DELETE Y SIN created_by (también deliberado):
--   La fila no se borra: la crea la migración y vive mientras exista la
--   plataforma; «borrar la configuración» es dejar el buzón en NULL, no marcar
--   deleted_at. Y no hay created_by porque a la única fila la siembra la
--   migración, no una persona: quién la editó por última vez sí se guarda
--   (updated_by), y el histórico completo lo lleva audit.audit_logs vía trigger.
--
-- IDEMPOTENTE EN LAS DOS MITADES: CREATE TABLE / CREATE INDEX / CREATE TRIGGER
-- llevan guarda, y el sembrado es un INSERT ... WHERE NOT EXISTS. Reaplicar el
-- script deja exactamente una fila con los mismos valores; no la duplica ni la
-- pisa (un UPSERT que sobreescribiera borraría el buzón ya configurado).
-- ─────────────────────────────────────────────────────────────────────────────

-- ============================================================================
-- admin.notification_test_settings — configuración del banco de pruebas (fila única)
-- ============================================================================
CREATE TABLE IF NOT EXISTS admin.notification_test_settings (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_notification_test_settings PRIMARY KEY (id),

    -- Buzón de pruebas. NULL = todavía no configurado; el banco de pruebas no
    -- tiene a dónde enviar y la HU #11368 lo rechazará con un error de negocio.
    -- Un default '' fingiría estar configurado, así que no lo hay.
    test_recipient_email varchar(320),

    -- Sello del último envío de prueba. NULL = nunca se ha enviado uno. Lo
    -- escribe la HU #11368; aquí solo existe la columna donde se sella.
    last_test_sent_at timestamptz,

    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz,
    updated_by uuid
);

-- Fila única: la configuración es global, no una lista. El índice sobre la
-- expresión constante `true` da a TODAS las filas la misma llave, así que la BD
-- rechaza la segunda con violación de unicidad. Es el mismo truco de
-- uq_quipux_settings_singleton.
CREATE UNIQUE INDEX IF NOT EXISTS uq_notification_test_settings_singleton
  ON admin.notification_test_settings ((true));

-- row_version lo lleva el motor, no la aplicación: el token de concurrencia
-- optimista (ver NotificationTestSettingsConfiguration) tiene que incrementarse
-- también cuando el UPDATE viene de fuera de EF (un script, otra réplica).
-- Sin xmin: este repo no lo usa en ninguna tabla.
DROP TRIGGER IF EXISTS tr_notification_test_settings_row_version
  ON admin.notification_test_settings;
CREATE TRIGGER tr_notification_test_settings_row_version
  BEFORE UPDATE ON admin.notification_test_settings
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

-- Auditoría (checklist A16): updated_by solo recuerda al ÚLTIMO que editó. El
-- buzón de pruebas recibe correos reales, así que «quién lo cambió y a qué» debe
-- quedar en el histórico, no solo en la última versión de la fila.
DROP TRIGGER IF EXISTS tr_notification_test_settings_audit
  ON admin.notification_test_settings;
CREATE TRIGGER tr_notification_test_settings_audit
  AFTER INSERT OR UPDATE OR DELETE ON admin.notification_test_settings
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

COMMENT ON TABLE admin.notification_test_settings IS
  'HU #11365 (Feature #11349) — configuración del banco de pruebas de notificaciones: buzón de pruebas y marca del último envío. Fila única (uq_notification_test_settings_singleton). GLOBAL DE PLATAFORMA: sin tenant_id y, en consecuencia, sin RLS — igual que admin.quipux_settings. La consume la HU #11366 (endpoints) y la sella la HU #11368 (envío + límite de frecuencia).';

COMMENT ON COLUMN admin.notification_test_settings.test_recipient_email IS
  '@pii:medium — dirección de correo del buzón de pruebas (Ley 1581). Finalidad: recibir los envíos de prueba de plantillas de notificación para verificar su contenido y formato. No usar para comunicaciones operativas ni para contactar al titular. NULL = banco de pruebas sin configurar.';
COMMENT ON COLUMN admin.notification_test_settings.last_test_sent_at IS
  'Marca del último envío de prueba. Sostiene el límite de frecuencia de la HU #11368; vive en BD para sobrevivir a un reinicio y valer para todas las réplicas. NULL = nunca se envió una prueba.';
COMMENT ON COLUMN admin.notification_test_settings.row_version IS
  'Token de concurrencia optimista. Lo incrementa el trigger tr_notification_test_settings_row_version, no la aplicación.';
COMMENT ON COLUMN admin.notification_test_settings.updated_by IS
  'SuperAdmin que dejó la configuración como está. NULL en la fila sembrada por la migración: no la editó nadie.';

-- Sembrado de la fila única. Con los dos campos en NULL: la migración no
-- inventa un buzón ni finge un envío que no ocurrió.
-- WHERE NOT EXISTS (y no ON CONFLICT DO UPDATE) para que la segunda pasada no
-- toque la fila: si alguien ya configuró el buzón, reaplicar el script no puede
-- borrárselo.
INSERT INTO admin.notification_test_settings (test_recipient_email, last_test_sent_at)
SELECT NULL, NULL
WHERE NOT EXISTS (SELECT 1 FROM admin.notification_test_settings);
