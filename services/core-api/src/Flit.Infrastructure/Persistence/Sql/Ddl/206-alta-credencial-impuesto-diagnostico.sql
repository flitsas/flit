-- HU #14711 (Feature #14693) — alta idempotente de la credencial del proveedor de impuesto y webhook en Diagnóstico.
-- Plan: docs/diagnosticos/plan-alta-credencial-impuesto-diagnostico.md §3.1. ADR-0092 (Propuesto). Migración:
-- 20261125231833_HU14711_AltaCredencialImpuestoDiagnostico.
--
-- Qué hace:
--   1. ck_tenant_product_credentials_code (DDL 204) admite, además de ('diagnostico','impuesto_api_key'), los dos secretos del
--      webhook que trae el alta del proveedor: ('diagnostico','impuesto_webhook_secret') y
--      ('diagnostico','impuesto_webhook_secret_anterior'). El anterior se guarda al rotar el secreto y vale 24 h desde su
--      updated_at. La tabla es clave/valor: sin columnas nuevas.
--   2. platform.tenant_product_provisioning (DDL 205) suma attempts (intentos automáticos fallidos, >= 0) y next_attempt_at
--      (próximo intento o fin de la reserva) para el alta automática de diagnostico, que la plataforma escribe ella misma
--      (product_code = 'diagnostico'). Índice parcial para la ronda del trabajador (status = 'PENDIENTE'), con el mismo orden
--      que la reserva (next_attempt_at NULLS FIRST). La reserva corre con app.is_superadmin en su propia transacción.
--
-- Checklist de esquema: sin tabla nueva (A1–A16 ya cumplidas o exceptuadas en DDL 204/205; A6/A16 de credenciales en
-- ADR-0092). A11: el índice nuevo es de la ronda del trabajador, que lee entre empresas por producto y fecha (no por empresa);
-- el acceso por empresa sigue cubierto por uq_tenant_product_provisioning_tenant_product.
--
-- Idempotente (DROP … IF EXISTS / ADD COLUMN IF NOT EXISTS / DROP INDEX IF EXISTS + CREATE INDEX: así una base con la versión
-- anterior del índice queda con la definición vigente). Down en la migración: borra SOLO las
-- filas de los dos secretos (se recuperan repitiendo el alta, que es idempotente) antes de restaurar el CHECK del DDL 204, y
-- quita el índice, el CHECK y las dos columnas.

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- 1. Lista blanca de credenciales
-- ─────────────────────────────────────────────────────────────────────────────────────────────
ALTER TABLE platform.tenant_product_credentials DROP CONSTRAINT IF EXISTS ck_tenant_product_credentials_code;
ALTER TABLE platform.tenant_product_credentials ADD CONSTRAINT ck_tenant_product_credentials_code
    CHECK ((product_code, credential_code) IN (
        ('diagnostico', 'impuesto_api_key'),
        ('diagnostico', 'impuesto_webhook_secret'),
        ('diagnostico', 'impuesto_webhook_secret_anterior')));

COMMENT ON TABLE platform.tenant_product_credentials IS
    'ADR-0092 · Credencial de un producto por empresa (diagnostico: impuesto_api_key, impuesto_webhook_secret e impuesto_webhook_secret_anterior, HU #14711). Solo la plataforma (core-identity / core-api) la lee y la escribe. La UI solo ve configurada/no configurada.';
COMMENT ON COLUMN platform.tenant_product_credentials.credential_code IS
    '@pii:none · impuesto_api_key (la gestiona el SuperAdmin o el alta del proveedor), impuesto_webhook_secret e impuesto_webhook_secret_anterior (solo el alta; el anterior vale 24 h desde su updated_at).';

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- 2. Intentos y próximo intento del alta automática
-- ─────────────────────────────────────────────────────────────────────────────────────────────
ALTER TABLE platform.tenant_product_provisioning
    ADD COLUMN IF NOT EXISTS attempts        integer     NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS next_attempt_at timestamptz NULL;

ALTER TABLE platform.tenant_product_provisioning DROP CONSTRAINT IF EXISTS ck_tenant_product_provisioning_attempts;
ALTER TABLE platform.tenant_product_provisioning ADD CONSTRAINT ck_tenant_product_provisioning_attempts CHECK (attempts >= 0);

DROP INDEX IF EXISTS platform.ix_tenant_product_provisioning_product_code_next_attempt_at;
CREATE INDEX ix_tenant_product_provisioning_product_code_next_attempt_at
    ON platform.tenant_product_provisioning (product_code, next_attempt_at NULLS FIRST)
    WHERE status = 'PENDIENTE';

COMMENT ON COLUMN platform.tenant_product_provisioning.attempts IS
    '@pii:none · HU #14711 · Intentos automáticos fallidos del alta que escribe la plataforma (diagnostico). 0 en los demás productos.';
COMMENT ON COLUMN platform.tenant_product_provisioning.next_attempt_at IS
    '@pii:none · HU #14711 · Próximo intento del alta automática (espera escalonada 1/5/15/60 min) o fin de la reserva de la ronda. NULL fuera de PENDIENTE.';
