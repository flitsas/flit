-- HU #12968 (Feature #12888, FLIT Suite, tarea B-08) — el dominio de una red dice qué sirve: su hub o un producto.
-- Migración: 20260925210000_HU12968_TenantDomainsPurpose · contrato de plataforma v1, §5 · ADR-0060.
--
-- Qué hace:
--   1. admin.tenant_domains.purpose: HUB (hub y login de la red, lo que existe hoy) o el código de un producto
--      del catálogo (platform.products). Todas las filas actuales quedan en HUB.
--   2. La unicidad pasa de «un dominio vigente por red» a «uno por red y propósito»: una red puede tener
--      cliente.com (HUB) y tramites.cliente.com (tramites). El host sigue siendo único en toda la plataforma.
--   3. admin.v_active_network_domains expone purpose; DomainContext lo usa para el producto de la petición.
--
-- El registro, la verificación y el retiro que ya existen siguen operando sobre el dominio HUB. Idempotente;
-- el Down cierra los dominios de producto y vuelve al índice anterior.

ALTER TABLE admin.tenant_domains ADD COLUMN IF NOT EXISTS purpose varchar(40) NOT NULL DEFAULT 'HUB';

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_tenant_domains_purpose') THEN
        ALTER TABLE admin.tenant_domains ADD CONSTRAINT ck_tenant_domains_purpose
            CHECK (purpose = 'HUB' OR (purpose ~ '^[a-z][a-z0-9]{1,39}$' AND purpose <> 'plataforma'));
    END IF;
END $$;

-- Un propósito que no es HUB tiene que ser un producto del catálogo.
CREATE OR REPLACE FUNCTION admin.trg_tenant_domains_purpose_product()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.purpose <> 'HUB' AND NOT EXISTS (SELECT 1 FROM platform.products WHERE code = NEW.purpose) THEN
        RAISE EXCEPTION 'El propósito % no es un producto del catálogo', NEW.purpose
            USING ERRCODE = 'check_violation', CONSTRAINT = 'ck_tenant_domains_purpose_product';
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS tr_tenant_domains_purpose_product ON admin.tenant_domains;
CREATE TRIGGER tr_tenant_domains_purpose_product
    BEFORE INSERT OR UPDATE OF purpose ON admin.tenant_domains
    FOR EACH ROW EXECUTE FUNCTION admin.trg_tenant_domains_purpose_product();

DROP INDEX IF EXISTS admin.uq_tenant_domains_tenant_id;
CREATE UNIQUE INDEX IF NOT EXISTS uq_tenant_domains_tenant_purpose
    ON admin.tenant_domains (tenant_id, purpose) WHERE deleted_at IS NULL;

COMMENT ON COLUMN admin.tenant_domains.purpose IS
    'HU #12968 (contrato v1 §5) · HUB = hub y login de la red; si no, el código del producto que sirve este host. Un dominio vigente por (tenant_id, purpose).';

-- Se agrega la columna al final: CREATE OR REPLACE VIEW lo permite sin tocar a sus consumidores.
CREATE OR REPLACE VIEW admin.v_active_network_domains AS
SELECT d.host,
       d.tenant_id AS head_tenant_id,
       d.purpose
  FROM admin.tenant_domains d
  JOIN identity.tenants t ON t.id = d.tenant_id
 WHERE d.status = 'active'
   AND d.deleted_at IS NULL
   AND t.tenant_type = 'MARCA_BLANCA'
   AND t.is_group_parent = true
   AND t.is_active = true;
