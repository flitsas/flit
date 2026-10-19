-- HU #13642 (Feature #13101 F-B, Épica #12753) — permisos individuales por usuario (ADR-0071, opción A).
-- Migración: 20261019140000_HU13642_PermisosIndividuales
--
--  * security.user_permission_overrides: un hecho por (tenant, usuario, producto, permiso) con efecto allow|deny.
--    Unicidad PARCIAL (deleted_at IS NULL): revocar es soft delete y deja volver a otorgar. "Módulo completo" = una
--    fila por acción existente al otorgar (source = MODULE_SNAPSHOT, batch_id agrupa la foto); una acción agregada
--    después no queda otorgada.
--  * security.user_permission_audit: bitácora de negocio APPEND-ONLY (UPDATE/DELETE rechazados por trigger, también
--    para el owner). EXCEPCIÓN A6: sin soft delete ni updated_*/row_version (nunca se modifica). Cubre éxitos y
--    rechazos (outcome/reason_code), purgas por cambio de rol y la confirmación de ict.pii.reveal (action libre).
--  * RLS tenant_isolation sin FORCE (convención del repo: la app conecta como owner y el contexto lo fija la app).

CREATE TABLE IF NOT EXISTS security.user_permission_overrides (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_user_permission_overrides PRIMARY KEY (id),
    tenant_id uuid NOT NULL,
    user_id uuid NOT NULL,
    product_code varchar(40) NOT NULL,
    permission_id uuid NOT NULL,
    effect varchar(5) NOT NULL,
    source varchar(16) NOT NULL DEFAULT 'INDIVIDUAL',
    batch_id uuid NULL,
    granted_by uuid NOT NULL,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    updated_at timestamptz NULL,
    updated_by uuid NULL,
    deleted_at timestamptz NULL,
    deleted_by uuid NULL,
    CONSTRAINT fk_user_permission_overrides_tenants FOREIGN KEY (tenant_id)
        REFERENCES identity.tenants (id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT fk_user_permission_overrides_users FOREIGN KEY (user_id)
        REFERENCES identity.users (id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT fk_user_permission_overrides_permissions FOREIGN KEY (permission_id)
        REFERENCES security.permissions (id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT fk_user_permission_overrides_products FOREIGN KEY (product_code)
        REFERENCES platform.products (code) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT ck_user_permission_overrides_effect CHECK (effect IN ('allow', 'deny')),
    CONSTRAINT ck_user_permission_overrides_source CHECK (source IN ('INDIVIDUAL', 'MODULE_SNAPSHOT'))
);

-- Unicidad y ruta caliente (emisión de token, y borrado por tenant+usuario+producto al cambiar de rol).
CREATE UNIQUE INDEX IF NOT EXISTS uq_user_permission_overrides_active
    ON security.user_permission_overrides (tenant_id, user_id, product_code, permission_id) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS ix_user_permission_overrides_user_id ON security.user_permission_overrides (user_id);
CREATE INDEX IF NOT EXISTS ix_user_permission_overrides_permission_id ON security.user_permission_overrides (permission_id);
CREATE INDEX IF NOT EXISTS ix_user_permission_overrides_product_code ON security.user_permission_overrides (product_code);

ALTER TABLE security.user_permission_overrides ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON security.user_permission_overrides;
CREATE POLICY tenant_isolation ON security.user_permission_overrides
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP TRIGGER IF EXISTS tr_user_permission_overrides_row_version ON security.user_permission_overrides;
CREATE TRIGGER tr_user_permission_overrides_row_version BEFORE UPDATE ON security.user_permission_overrides
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_user_permission_overrides_audit ON security.user_permission_overrides;
CREATE TRIGGER tr_user_permission_overrides_audit AFTER INSERT OR UPDATE OR DELETE ON security.user_permission_overrides
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

CREATE TABLE IF NOT EXISTS security.user_permission_audit (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_user_permission_audit PRIMARY KEY (id),
    tenant_id uuid NOT NULL,
    actor_user_id uuid NOT NULL,
    target_user_id uuid NOT NULL,
    product_code varchar(40) NULL,
    module_code varchar(50) NULL,
    permission_slug varchar(120) NULL,
    action varchar(32) NOT NULL,
    outcome varchar(10) NOT NULL DEFAULT 'OK',
    reason_code varchar(40) NULL,
    value_before varchar(5) NULL,
    value_after varchar(5) NULL,
    batch_id uuid NULL,
    correlation_id uuid NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT fk_user_permission_audit_tenants FOREIGN KEY (tenant_id)
        REFERENCES identity.tenants (id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT ck_user_permission_audit_outcome CHECK (outcome IN ('OK', 'REJECTED')),
    CONSTRAINT ck_user_permission_audit_values CHECK (
        (value_before IS NULL OR value_before IN ('allow', 'deny'))
        AND (value_after IS NULL OR value_after IN ('allow', 'deny')))
);

CREATE INDEX IF NOT EXISTS ix_user_permission_audit_tenant_target
    ON security.user_permission_audit (tenant_id, target_user_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_user_permission_audit_tenant_actor
    ON security.user_permission_audit (tenant_id, actor_user_id, created_at DESC);

CREATE OR REPLACE FUNCTION security.trg_user_permission_audit_immutable() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'security.user_permission_audit es append-only: no se permite % (id=%)', TG_OP, OLD.id
        USING ERRCODE = 'check_violation';
END; $$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tr_user_permission_audit_immutable ON security.user_permission_audit;
CREATE TRIGGER tr_user_permission_audit_immutable BEFORE UPDATE OR DELETE ON security.user_permission_audit
    FOR EACH ROW EXECUTE FUNCTION security.trg_user_permission_audit_immutable();

ALTER TABLE security.user_permission_audit ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON security.user_permission_audit;
CREATE POLICY tenant_isolation ON security.user_permission_audit
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

COMMENT ON TABLE security.user_permission_overrides IS
    'HU #13642 / ADR-0071 — permiso individual de un usuario en un tenant y producto: efecto allow|deny sobre un permiso del catálogo. Unicidad parcial por usuario+tenant+producto+permiso (activos). Módulo completo = una fila por acción al otorgar (source MODULE_SNAPSHOT).';
COMMENT ON COLUMN security.user_permission_overrides.product_code IS 'Producto del módulo del permiso (platform.products.code).';
COMMENT ON COLUMN security.user_permission_overrides.effect IS 'allow | deny. deny gana sobre el rol; no pueden coexistir allow y deny del mismo permiso.';
COMMENT ON COLUMN security.user_permission_overrides.source IS 'INDIVIDUAL | MODULE_SNAPSHOT (foto de las acciones del módulo al otorgar).';
COMMENT ON COLUMN security.user_permission_overrides.batch_id IS 'Agrupa las filas de una misma foto de módulo completo.';
COMMENT ON COLUMN security.user_permission_overrides.granted_by IS 'Usuario actor que otorgó el permiso (identity.users.id).';
COMMENT ON TABLE security.user_permission_audit IS
    'HU #13642 / ADR-0071 — bitácora append-only de permisos individuales: éxitos, rechazos con motivo, purgas por cambio de rol y confirmación de ict.pii.reveal. UPDATE/DELETE rechazados por tr_user_permission_audit_immutable. Solo identificadores, sin PII.';
COMMENT ON COLUMN security.user_permission_audit.action IS 'GRANT | DENY | REVOKE | MODULE_SNAPSHOT | ROLE_CHANGE_PURGE | PII_REVEAL_CONFIRMED (vocabulario abierto, sin CHECK).';
COMMENT ON COLUMN security.user_permission_audit.outcome IS 'OK | REJECTED.';
COMMENT ON COLUMN security.user_permission_audit.reason_code IS 'Motivo del rechazo (OUT_OF_CAP, SELF_EDIT, ADMIN_TARGET, INVITATION_PENDING, NOT_ENFORCED, EXCLUDED, TOKEN_BUDGET, OUT_OF_SCOPE); NULL si OK.';
COMMENT ON COLUMN security.user_permission_audit.value_before IS 'Efecto previo (allow|deny) o NULL si no había override.';
COMMENT ON COLUMN security.user_permission_audit.value_after IS 'Efecto resultante (allow|deny) o NULL si se revocó/eliminó.';
