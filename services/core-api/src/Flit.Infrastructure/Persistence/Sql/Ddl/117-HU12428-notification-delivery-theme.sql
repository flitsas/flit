-- =====================================================================================================
-- 117-HU12428-notification-delivery-theme.sql  (traza del tema y remitente aplicados; #12428 AC5, #12430 AC5)
-- =====================================================================================================
ALTER TABLE admin.notification_delivery_logs
    ADD COLUMN IF NOT EXISTS theme_kind    varchar(10)  NULL,   -- flit | brand
    ADD COLUMN IF NOT EXISTS theme_version integer      NULL,   -- tenant_brandings.published_version (NULL con flit)
    ADD COLUMN IF NOT EXISTS sender_name   varchar(80)  NULL,   -- nombre visible aplicado
    ADD COLUMN IF NOT EXISTS sender_email  varchar(320) NULL;   -- dirección aplicada (siempre la de FLIT en FlitSmtp)

ALTER TABLE admin.notification_delivery_logs
    DROP CONSTRAINT IF EXISTS ck_notification_delivery_logs_theme_kind,
    ADD  CONSTRAINT ck_notification_delivery_logs_theme_kind CHECK (theme_kind IS NULL OR theme_kind IN ('flit', 'brand'));

COMMENT ON COLUMN admin.notification_delivery_logs.theme_kind    IS 'ADR-0060 · Tema aplicado al correo: flit | brand. NULL en filas anteriores a #12428.';
COMMENT ON COLUMN admin.notification_delivery_logs.theme_version IS 'published_version de la marca aplicada (solo brand).';
COMMENT ON COLUMN admin.notification_delivery_logs.sender_name   IS 'Nombre visible del remitente aplicado (saneado). @pii:low';
COMMENT ON COLUMN admin.notification_delivery_logs.sender_email  IS 'Dirección de remitente aplicada. @pii:low';
