-- HU #14694 (Epic #14692, producto Impuestos) — ADR-0093 (Propuesto), plan docs/impuestos/plan-implementacion.md §4.3.
-- Migración: 20261125041316_ImpuestosProductoYAprovisionamiento.
--
-- Qué hace:
--   1. Producto `impuestos` en el catálogo (icono lucide `receipt`, orden 60) con status = 'inactive' (BLOQ-1 a): no se
--      puede encender para ninguna empresa (PUT tenant products ⇒ 409 PRODUCT_INACTIVE) ni registrar su manifiesto hasta
--      activarlo en cada ambiente (docs/impuestos/guia-despliegue.md §1). Sin filas en tenant_products: además nace apagado
--      para todas las empresas (fail-closed); lo enciende el SuperAdmin.
--   2. Rol de sistema admin_impuestos (COMPANY), con el molde del DDL 126 §1, y siembra a cada AdminCompany activo (DDL 126
--      §2). Sin permisos: los trae el manifiesto del producto (PUT /platform/products/impuestos/manifest), que también crea
--      gestor_impuestos. El acceso sigue exigiendo el producto encendido (ProductAccessResolver).
--   3. Ascenso de Gestor a Admin, SOLO en impuestos (F5b-CF2, A-H16, gate LT G-3): uq_ura_active_user_tenant_product
--      (DDL 120) admite un solo rol activo por usuario, empresa y producto, así que el espejo genérico del DDL 126 no le
--      daría admin_impuestos a un AdminCompany que ya es gestor_impuestos. Este DDL cierra (soft delete) ese
--      gestor_impuestos con deleted_by = 00000000-0000-4000-8000-000000000205 (actor de sistema del DDL 205, constante
--      ImpuestosProductoYAprovisionamiento.ActorAscensoDdl205; el Down reabre solo esas filas) y le da admin_impuestos, y
--      security.trg_ura_mirror_product_admins() suma una rama que hace lo mismo al asignar AdminCompany. Los demás productos conservan el comportamiento del DDL 126 (el rol que ya hay se respeta).
--   4. platform.tenant_product_provisioning: último estado de aprovisionamiento que reporta el servicio del producto (rpc
--      flit.identidad.v1.AprovisionamientoProductoService.ReportarEstadoAprovisionamiento, versión monotónica) para el
--      admin de compañías (decisión R1, plan §1.2). Una fila por empresa y producto.
--   5. Sin cambio en el CHECK del DDL 204 (DA-2=b): las credenciales de Impuestos viven en core-impuestos.
--
-- Excepciones al checklist de esquema (pendientes de registrar en ADR-0093, que hoy no las menciona; las propone este DDL):
--   A5/A6 sin created_by/updated_by ni soft delete en tenant_product_provisioning: la escribe un servicio (no un usuario)
--         con upsert por versión; el rastro técnico queda en audit.audit_logs (trg_audit_log). Columnas en inglés como el
--         resto del esquema (decisión del usuario, 2026-11-25); el contrato gRPC que las alimenta sigue en español.
--   A11  el índice único (tenant_id, product_code) ya empieza por tenant_id; ix_..._product_code cubre la FK al catálogo.
--
-- Idempotente (ON CONFLICT / NOT EXISTS / IF NOT EXISTS / CREATE OR REPLACE / DROP ... IF EXISTS) y reversible (Down en
-- la migración).

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- 1. Producto impuestos
-- ─────────────────────────────────────────────────────────────────────────────────────────────
INSERT INTO platform.products (code, name, icon, status, sort_order)
VALUES ('impuestos', 'Impuestos', 'receipt', 'inactive', 60)
ON CONFLICT (code) DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- 2. Rol admin_impuestos
-- ─────────────────────────────────────────────────────────────────────────────────────────────
INSERT INTO security.roles (code, name, description, is_system, is_active, target_entity_type, product_code)
SELECT 'admin_impuestos', 'Administrador Impuestos',
       'Administra Impuestos en la empresa. Acompaña siempre a AdminCompany (HU #14694, DDL 205).', true, true, 'COMPANY',
       'impuestos'
 WHERE NOT EXISTS (
       SELECT 1 FROM security.roles r
        WHERE r.code = 'admin_impuestos' AND r.target_entity_type = 'COMPANY' AND r.tenant_id IS NULL AND r.deleted_at IS NULL);

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- 3a. AdminCompany activo que ya es gestor_impuestos ⇒ se cierra el gestor (ascenso; solo impuestos). deleted_by es el
--     actor de sistema del DDL 205 (00000000-0000-4000-8000-000000000205): el Down reabre solo estas filas.
-- ─────────────────────────────────────────────────────────────────────────────────────────────
UPDATE security.user_role_assignments g
   SET deleted_at = now(), deleted_by = '00000000-0000-4000-8000-000000000205'::uuid
  FROM security.roles gr
 WHERE gr.id = g.role_id AND gr.code = 'gestor_impuestos' AND gr.product_code = 'impuestos' AND gr.tenant_id IS NULL
   AND g.deleted_at IS NULL
   AND EXISTS (
       SELECT 1 FROM security.user_role_assignments a
         JOIN security.roles ac ON ac.id = a.role_id AND ac.code = 'AdminCompany' AND ac.target_entity_type = 'COMPANY'
                               AND ac.deleted_at IS NULL
        WHERE a.user_id = g.user_id AND a.tenant_id = g.tenant_id AND a.deleted_at IS NULL);

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- 3b. AdminCompany activo ⇒ admin_impuestos (los que falten; DDL 126 §2)
-- ─────────────────────────────────────────────────────────────────────────────────────────────
INSERT INTO security.user_role_assignments (tenant_id, user_id, role_id, assigned_at)
SELECT a.tenant_id, a.user_id, ai.id, now()
  FROM security.user_role_assignments a
  JOIN security.roles ac ON ac.id = a.role_id AND ac.code = 'AdminCompany' AND ac.target_entity_type = 'COMPANY'
 CROSS JOIN LATERAL (
       SELECT id FROM security.roles
        WHERE code = 'admin_impuestos' AND target_entity_type = 'COMPANY' AND tenant_id IS NULL AND is_system
          AND deleted_at IS NULL
        LIMIT 1) ai
 WHERE a.deleted_at IS NULL
   AND NOT EXISTS (
       SELECT 1 FROM security.user_role_assignments x
        WHERE x.user_id = a.user_id AND x.tenant_id = a.tenant_id
          AND x.product_code = 'impuestos' AND x.deleted_at IS NULL);

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- 3c. Espejo AdminCompany ⇒ admin de cada producto, con la rama de ascenso solo para impuestos
--     (reemplaza la versión del DDL 126; lo demás queda igual)
-- ─────────────────────────────────────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION security.trg_ura_mirror_product_admins()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_admin_company uuid;
    v_was_active boolean;
    v_is_active boolean;
BEGIN
    SELECT id INTO v_admin_company FROM security.roles
     WHERE code = 'AdminCompany' AND target_entity_type = 'COMPANY' AND deleted_at IS NULL LIMIT 1;
    IF v_admin_company IS NULL THEN
        RETURN NULL;
    END IF;

    v_is_active  := NEW.deleted_at IS NULL AND NEW.role_id = v_admin_company;
    v_was_active := TG_OP = 'UPDATE' AND OLD.deleted_at IS NULL AND OLD.role_id = v_admin_company;

    IF v_is_active AND NOT v_was_active THEN
        -- HU #14694 (DDL 205, solo impuestos): el AdminCompany que ya era gestor_impuestos asciende. Se cierra el gestor
        -- antes del alta genérica para que el índice de un rol activo por producto deje entrar a admin_impuestos.
        IF EXISTS (SELECT 1 FROM security.roles
                    WHERE code = 'admin_impuestos' AND target_entity_type = 'COMPANY' AND tenant_id IS NULL AND is_system
                      AND deleted_at IS NULL) THEN
            UPDATE security.user_role_assignments g
               SET deleted_at = now(), deleted_by = NEW.assigned_by
              FROM security.roles gr
             WHERE gr.id = g.role_id AND gr.code = 'gestor_impuestos' AND gr.product_code = 'impuestos'
               AND gr.tenant_id IS NULL
               AND g.user_id = NEW.user_id AND g.tenant_id = NEW.tenant_id AND g.deleted_at IS NULL;
        END IF;

        -- Se asigna AdminCompany: el admin de cada producto en el que el usuario todavía no tenga rol.
        INSERT INTO security.user_role_assignments (tenant_id, user_id, role_id, assigned_at, assigned_by, created_by)
        SELECT NEW.tenant_id, NEW.user_id, pa.id, now(), NEW.assigned_by, NEW.created_by
          FROM platform.products p
          JOIN security.roles pa ON pa.code = 'admin_' || p.code AND pa.target_entity_type = 'COMPANY'
                                 AND pa.is_system AND pa.deleted_at IS NULL
         WHERE p.code <> 'plataforma'
           AND NOT EXISTS (
               SELECT 1 FROM security.user_role_assignments x
                WHERE x.user_id = NEW.user_id AND x.tenant_id = NEW.tenant_id
                  AND x.product_code = p.code AND x.deleted_at IS NULL);
    ELSIF v_was_active AND NOT v_is_active THEN
        -- Se quita AdminCompany: se cierran los admin de producto que lo acompañaban.
        UPDATE security.user_role_assignments u
           SET deleted_at = COALESCE(NEW.deleted_at, now()), deleted_by = NEW.deleted_by
          FROM security.roles pa
          JOIN platform.products p ON pa.code = 'admin_' || p.code AND p.code <> 'plataforma'
         WHERE u.role_id = pa.id AND pa.is_system AND pa.target_entity_type = 'COMPANY'
           AND u.user_id = NEW.user_id AND u.tenant_id = NEW.tenant_id AND u.deleted_at IS NULL;
    END IF;

    RETURN NULL;
END;
$$;

COMMENT ON FUNCTION security.trg_ura_mirror_product_admins() IS
    'HU #12967 · El admin de cada producto (admin_<código>, de sistema) acompaña a AdminCompany: se crea al asignarlo y se cierra al quitarlo. HU #14694 (DDL 205): solo en impuestos, un gestor_impuestos activo se cierra y asciende a admin_impuestos.';

-- El disparador del DDL 126 ya apunta a esta función: no se recrea.

-- ─────────────────────────────────────────────────────────────────────────────────────────────
-- 4. platform.tenant_product_provisioning
-- ─────────────────────────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS platform.tenant_product_provisioning (
    id               uuid         NOT NULL DEFAULT uuidv7(),
    tenant_id        uuid         NOT NULL,
    product_code     varchar(40)  NOT NULL,
    status              varchar(32)  NOT NULL,
    reason_code         varchar(64)  NULL,
    message             varchar(300) NULL,
    action              varchar(32)  NULL,
    signature_confirmed boolean      NULL,
    reported_version    bigint       NOT NULL,
    reported_at         timestamptz  NOT NULL,
    created_at          timestamptz  NOT NULL DEFAULT now(),
    row_version         bigint       NOT NULL DEFAULT 0,

    CONSTRAINT pk_tenant_product_provisioning PRIMARY KEY (id),
    CONSTRAINT uq_tenant_product_provisioning_tenant_product UNIQUE (tenant_id, product_code),
    CONSTRAINT fk_tenant_product_provisioning_tenants FOREIGN KEY (tenant_id)
        REFERENCES identity.tenants (id) ON DELETE CASCADE ON UPDATE RESTRICT,
    CONSTRAINT fk_tenant_product_provisioning_products FOREIGN KEY (product_code)
        REFERENCES platform.products (code) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT ck_tenant_product_provisioning_not_plataforma CHECK (product_code <> 'plataforma'),
    CONSTRAINT ck_tenant_product_provisioning_reported_version CHECK (reported_version >= 1)
);

CREATE INDEX IF NOT EXISTS ix_tenant_product_provisioning_product_code
    ON platform.tenant_product_provisioning (product_code);

COMMENT ON TABLE platform.tenant_product_provisioning IS
    'HU #14694 (ADR-0093) · Último estado de aprovisionamiento de un producto por empresa, reportado por el servicio del producto (rpc ReportarEstadoAprovisionamiento). Solo se guarda una versión mayor que la vigente. Lo lee GET /platform/admin/tenants/{id}/products (campo provisioning).';
COMMENT ON COLUMN platform.tenant_product_provisioning.status IS
    '@pii:none · Estado que define el producto. En impuestos: PENDIENTE, LISTO, LISTO_SIN_WEBHOOK, ERROR, INCIERTO, REQUIERE_CREDENCIALES, REQUIERE_REVISION_NIT o SUSPENDIDO (plan §3.1). Sin CHECK: la tabla sirve a cualquier producto con aprovisionamiento.';
COMMENT ON COLUMN platform.tenant_product_provisioning.reason_code IS
    '@pii:none · Código estable del motivo (MotivoAprovisionamiento de contracts/openapi/impuestos.v1.yaml).';
COMMENT ON COLUMN platform.tenant_product_provisioning.message IS
    '@pii:none · Motivo legible (literal cerrado del motivo). Nunca secretos ni datos personales.';
COMMENT ON COLUMN platform.tenant_product_provisioning.action IS
    '@pii:none · Acción del SuperAdmin: REINTENTAR, CARGAR_SECRETO, CARGAR_CREDENCIALES o CORREGIR_NIT. NULL = ninguna.';
COMMENT ON COLUMN platform.tenant_product_provisioning.signature_confirmed IS
    'Solo en LISTO: false = «Firma sin confirmar» hasta el primer webhook con firma válida. NULL en los demás estados.';
COMMENT ON COLUMN platform.tenant_product_provisioning.reported_version IS
    'Versión monotónica del estado en el servicio del producto (idempotencia del reporte). No es row_version.';
COMMENT ON COLUMN platform.tenant_product_provisioning.reported_at IS
    'Momento del estado en el servicio del producto.';
COMMENT ON COLUMN platform.tenant_product_provisioning.row_version IS
    'Token de concurrencia (public.trg_row_version).';

DROP TRIGGER IF EXISTS tr_tenant_product_provisioning_row_version ON platform.tenant_product_provisioning;
CREATE TRIGGER tr_tenant_product_provisioning_row_version BEFORE UPDATE ON platform.tenant_product_provisioning
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_tenant_product_provisioning_audit ON platform.tenant_product_provisioning;
CREATE TRIGGER tr_tenant_product_provisioning_audit AFTER INSERT OR UPDATE OR DELETE ON platform.tenant_product_provisioning
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

ALTER TABLE platform.tenant_product_provisioning ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON platform.tenant_product_provisioning;
CREATE POLICY tenant_isolation ON platform.tenant_product_provisioning
    USING (
        current_setting('app.is_superadmin', true) = 'true'
        OR tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
    );

-- Down (en la migración): DROP TABLE platform.tenant_product_provisioning; restaura la función del DDL 126; borra las
-- asignaciones, las invitaciones (security.user_invitations, FK RESTRICT) y el rol admin_impuestos; reabre solo los
-- gestor_impuestos que cerró el 3a de este DDL (deleted_by = actor de sistema del DDL 205) y borra el producto (si nada lo
-- referencia).
