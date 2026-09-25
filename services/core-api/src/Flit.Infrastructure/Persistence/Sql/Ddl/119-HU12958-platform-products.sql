-- HU #12958 (Feature #12888, FLIT Suite frente B, tarea B-03) — productos de la suite y su habilitación por empresa.
-- Migración: 20260925160040_HU12958_PlatformProducts · ADR-0063 · contrato de plataforma v1, §1 y §4.
--
-- Qué crea:
--   1. Schema platform (plataforma de la suite: hub, productos y su habilitación).
--   2. platform.products: catálogo de productos (código del contrato §1, nombre, ícono lucide y estado
--      active|inactive). Semilla: plataforma, tramites, comparendos, diagnostico y demo. Sin tenant_id ni
--      RLS: es un catálogo global. Sin trg_audit_log porque su llave es code (la función exige id).
--   3. platform.tenant_products: un producto encendido o apagado por empresa. No es una suscripción: sin
--      estados, fechas, vencimientos ni cobro (contrato §4). Sin fila = apagado (fail-closed). La
--      auditoría legible (old/new por campo) la escribe el repositorio en admin.tenant_config_audit_logs
--      (EntityName=TenantProduct, FieldName ∈ {enabled, notes}); el rastro técnico, trg_audit_log.
--   4. Backfill: tramites encendido para todas las empresas que existen hoy (AC2): nadie pierde acceso.
--   5. platform.trg_tenant_default_products(): toda empresa nueva nace con tramites encendido, igual que
--      hoy todas usan Trámites. Transitorio: cuando el SuperAdmin elija los productos al crear la empresa
--      (B-12) este disparador se retira.
--
-- plataforma no lleva filas: es el hub y está encendido para todas (lo decide la aplicación).
-- La jerarquía (una hija solo tiene lo que su cabeza tiene encendido) la aplica el resolutor (B-05).
-- Idempotente (IF NOT EXISTS / ON CONFLICT / CREATE OR REPLACE / DROP ... IF EXISTS) y reversible (Down
-- completo en la migración). RLS tenant_isolation como el resto del repo, sin FORCE (la app conecta como
-- owner).

CREATE SCHEMA IF NOT EXISTS platform;

COMMENT ON SCHEMA platform IS
    'FLIT Suite (ADR-0061, ADR-0063) · Plataforma: catálogo de productos y su habilitación por empresa. Dueño: frente B.';

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- 1. platform.products — catálogo de productos de la suite
-- ─────────────────────────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS platform.products (
    code       varchar(40)  NOT NULL,
    name       varchar(100) NOT NULL,
    icon       varchar(60)  NOT NULL,
    status     varchar(20)  NOT NULL DEFAULT 'active',
    sort_order integer      NOT NULL DEFAULT 0,
    created_at timestamptz  NOT NULL DEFAULT now(),
    updated_at timestamptz  NOT NULL DEFAULT now(),

    CONSTRAINT pk_products PRIMARY KEY (code),
    -- Mismo formato que el aud del token y el subdominio: minúsculas, sin espacios ni guiones bajos.
    CONSTRAINT ck_products_code_format CHECK (code ~ '^[a-z][a-z0-9]{1,39}$'),
    CONSTRAINT ck_products_status CHECK (status IN ('active', 'inactive'))
);

COMMENT ON TABLE platform.products IS
    'ADR-0063 (HU #12958) · Productos de la suite. Lo que se enciende o apaga por empresa (platform.tenant_products); los permisos dentro de cada producto siguen siendo módulos (HU #10664).';
COMMENT ON COLUMN platform.products.code IS
    'Código del contrato de plataforma v1, §1: el mismo en el host, el schema, el aud del token y los roles.';
COMMENT ON COLUMN platform.products.icon IS
    'Nombre de ícono lucide que pinta el hub (GET /api/v1/platform/me/apps).';
COMMENT ON COLUMN platform.products.status IS
    'active | inactive. Un producto inactivo no se puede encender para ninguna empresa.';
COMMENT ON COLUMN platform.products.sort_order IS
    'Orden en el menú de productos del hub (el del contrato §1).';

INSERT INTO platform.products (code, name, icon, status, sort_order) VALUES
    ('plataforma',  'Plataforma',  'layout-grid',   'active', 10),
    ('tramites',    'Trámites',    'file-text',     'active', 20),
    ('comparendos', 'Comparendos', 'ticket',        'active', 30),
    ('diagnostico', 'Diagnóstico', 'gauge',         'active', 40),
    ('demo',        'Demo',        'flask-conical', 'active', 90)
ON CONFLICT (code) DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- 2. platform.tenant_products — producto encendido o apagado por empresa
-- ─────────────────────────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS platform.tenant_products (
    id           uuid         NOT NULL DEFAULT uuidv7(),
    tenant_id    uuid         NOT NULL,
    product_code varchar(40)  NOT NULL,
    enabled      boolean      NOT NULL,
    notes        varchar(500) NULL,
    created_at   timestamptz  NOT NULL DEFAULT now(),
    updated_at   timestamptz  NOT NULL DEFAULT now(),
    updated_by   uuid         NULL,
    row_version  bigint       NOT NULL DEFAULT 0,

    CONSTRAINT pk_tenant_products PRIMARY KEY (id),
    CONSTRAINT uq_tenant_products_tenant_product UNIQUE (tenant_id, product_code),
    CONSTRAINT fk_tenant_products_tenants FOREIGN KEY (tenant_id)
        REFERENCES identity.tenants (id) ON DELETE CASCADE ON UPDATE RESTRICT,
    CONSTRAINT fk_tenant_products_products FOREIGN KEY (product_code)
        REFERENCES platform.products (code) ON DELETE RESTRICT ON UPDATE RESTRICT,
    -- plataforma es el hub: siempre encendido, nunca se habilita por empresa.
    CONSTRAINT ck_tenant_products_not_plataforma CHECK (product_code <> 'plataforma')
);

-- Cobertura de la FK hacia el catálogo (la de tenant_id la cubre uq_tenant_products_tenant_product).
CREATE INDEX IF NOT EXISTS ix_tenant_products_product_code
    ON platform.tenant_products (product_code);

COMMENT ON TABLE platform.tenant_products IS
    'ADR-0063 (HU #12958) · Producto encendido o apagado por empresa (contrato v1 §4). Sin estados, fechas ni cobro. Sin fila = apagado (fail-closed). Cambios auditados en admin.tenant_config_audit_logs (EntityName=TenantProduct) por el repositorio.';
COMMENT ON COLUMN platform.tenant_products.enabled IS
    'Encendido: los usuarios de la empresa con rol en el producto pueden entrar. Apagado: nadie de la empresa entra, salvo el SuperAdmin (contrato §2.1).';
COMMENT ON COLUMN platform.tenant_products.notes IS
    'Motivo opcional que deja el SuperAdmin al encender o apagar.';
COMMENT ON COLUMN platform.tenant_products.updated_by IS
    'Usuario del último cambio. NULL = lo hizo una migración o el disparador de empresa nueva.';
COMMENT ON COLUMN platform.tenant_products.row_version IS
    'Token de concurrencia (public.trg_row_version).';

DROP TRIGGER IF EXISTS tr_tenant_products_row_version ON platform.tenant_products;
CREATE TRIGGER tr_tenant_products_row_version BEFORE UPDATE ON platform.tenant_products
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_tenant_products_audit ON platform.tenant_products;
CREATE TRIGGER tr_tenant_products_audit AFTER INSERT OR UPDATE OR DELETE ON platform.tenant_products
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

ALTER TABLE platform.tenant_products ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON platform.tenant_products;
CREATE POLICY tenant_isolation ON platform.tenant_products
    USING (
        current_setting('app.is_superadmin', true) = 'true'
        OR tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
    );

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- 3. Backfill: tramites encendido para todas las empresas existentes (AC2)
-- ─────────────────────────────────────────────────────────────────────────────────────────────
INSERT INTO platform.tenant_products (tenant_id, product_code, enabled, notes)
SELECT t.id, 'tramites', true, 'Habilitado por la migración de la suite (HU #12958)'
  FROM identity.tenants t
ON CONFLICT (tenant_id, product_code) DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- 4. Empresa nueva ⇒ tramites encendido (transitorio hasta B-12)
-- ─────────────────────────────────────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION platform.trg_tenant_default_products()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    INSERT INTO platform.tenant_products (tenant_id, product_code, enabled, notes)
    VALUES (NEW.id, 'tramites', true, 'Habilitado al crear la empresa (HU #12958)')
    ON CONFLICT (tenant_id, product_code) DO NOTHING;
    RETURN NEW;
END;
$$;

COMMENT ON FUNCTION platform.trg_tenant_default_products() IS
    'HU #12958 · Toda empresa nueva nace con tramites encendido, como hoy. Transitorio: se retira cuando el SuperAdmin elija los productos al crear la empresa (B-12).';

DROP TRIGGER IF EXISTS tr_tenants_default_products ON identity.tenants;
CREATE TRIGGER tr_tenants_default_products AFTER INSERT ON identity.tenants
    FOR EACH ROW EXECUTE FUNCTION platform.trg_tenant_default_products();
