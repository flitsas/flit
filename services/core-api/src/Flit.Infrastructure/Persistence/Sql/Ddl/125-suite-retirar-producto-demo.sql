-- Epic #13217 (FLIT Suite) — se retira el producto `demo` del catálogo (decisión de Samuel Cardenas, 2026-10-02:
-- los primeros productos nuevos son Comparendos y Diagnóstico; el producto de prueba no hace falta).
-- Migración: 20261002120000_Suite_RetirarProductoDemo.
--
-- Borra su habilitación por empresa, su cliente OIDC (con sus tokens y autorizaciones) y el producto. Si algún rol o
-- módulo quedó atado a `demo` (no debería: nunca tuvo app), el producto se deja inactivo en vez de borrarse, para no
-- romper esas filas; inactivo ya no sale en el hub ni se puede encender. Idempotente.

DELETE FROM platform.tenant_products WHERE product_code = 'demo';

DELETE FROM identity.oidc_tokens
WHERE application_id IN (SELECT id FROM identity.oidc_applications WHERE client_id = 'demo');
DELETE FROM identity.oidc_authorizations
WHERE application_id IN (SELECT id FROM identity.oidc_applications WHERE client_id = 'demo');
DELETE FROM identity.oidc_applications WHERE client_id = 'demo';

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM security.roles WHERE product_code = 'demo')
       OR EXISTS (SELECT 1 FROM security.modules WHERE product_code = 'demo') THEN
        UPDATE platform.products SET status = 'inactive', updated_at = now() WHERE code = 'demo';
    ELSE
        DELETE FROM platform.products WHERE code = 'demo';
    END IF;
END $$;
