-- HU #10148 — RBAC | Feature #10134

CREATE TABLE security.modules (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_modules PRIMARY KEY (id),
    code varchar(50) NOT NULL,
    name varchar(100) NOT NULL,
    description varchar(500),
    sort_order smallint NOT NULL DEFAULT 0,
    is_active boolean NOT NULL DEFAULT true,
    external_refs jsonb NOT NULL DEFAULT '{}',
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT uq_modules_code UNIQUE (code)
);

CREATE TABLE security.permissions (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_permissions PRIMARY KEY (id),
    module_id uuid NOT NULL REFERENCES security.modules(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    slug varchar(100) NOT NULL,
    name varchar(150) NOT NULL,
    http_method varchar(10) NOT NULL,
    route_pattern varchar(500) NOT NULL,
    scope varchar(20) NOT NULL DEFAULT 'tenant',
    is_active boolean NOT NULL DEFAULT true,
    external_refs jsonb NOT NULL DEFAULT '{}',
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT uq_permissions_slug UNIQUE (slug)
);
CREATE INDEX ix_permissions_module_id ON security.permissions(module_id);

CREATE TABLE security.roles (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_roles PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    code varchar(50) NOT NULL,
    name varchar(100) NOT NULL,
    description varchar(500),
    is_system boolean NOT NULL DEFAULT false,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    deleted_at timestamptz,
    deleted_by uuid,
    CONSTRAINT uq_roles_tenant_id_code UNIQUE (tenant_id, code)
);
CREATE INDEX ix_roles_tenant_id ON security.roles(tenant_id);

CREATE TABLE security.role_permissions (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_role_permissions PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    role_id uuid NOT NULL REFERENCES security.roles(id) ON DELETE CASCADE ON UPDATE CASCADE,
    permission_id uuid NOT NULL REFERENCES security.permissions(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    CONSTRAINT uq_role_permissions_role_id_permission_id UNIQUE (role_id, permission_id)
);
CREATE INDEX ix_role_permissions_tenant_id ON security.role_permissions(tenant_id);

CREATE TABLE security.user_role_assignments (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_user_role_assignments PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    user_id uuid NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE ON UPDATE CASCADE,
    role_id uuid NOT NULL REFERENCES security.roles(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    assigned_at timestamptz NOT NULL DEFAULT now(),
    assigned_by uuid REFERENCES identity.users(id),
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    deleted_at timestamptz,
    deleted_by uuid,
    CONSTRAINT uq_user_role_assignments_user_id_tenant_id UNIQUE (user_id, tenant_id)
);
CREATE INDEX ix_user_role_assignments_tenant_id ON security.user_role_assignments(tenant_id);

ALTER TABLE security.roles ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON security.roles
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
ALTER TABLE security.role_permissions ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON security.role_permissions
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
ALTER TABLE security.user_role_assignments ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON security.user_role_assignments
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
-- Triggers negocio (checklist A16)
DROP TRIGGER IF EXISTS tr_roles_row_version ON security.roles;
CREATE TRIGGER tr_roles_row_version BEFORE UPDATE ON security.roles
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_roles_audit ON security.roles;
CREATE TRIGGER tr_roles_audit AFTER INSERT OR UPDATE OR DELETE ON security.roles
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_role_permissions_audit ON security.role_permissions;
CREATE TRIGGER tr_role_permissions_audit AFTER INSERT OR UPDATE OR DELETE ON security.role_permissions
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_user_role_assignments_row_version ON security.user_role_assignments;
CREATE TRIGGER tr_user_role_assignments_row_version BEFORE UPDATE ON security.user_role_assignments
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_user_role_assignments_audit ON security.user_role_assignments;
CREATE TRIGGER tr_user_role_assignments_audit AFTER INSERT OR UPDATE OR DELETE ON security.user_role_assignments
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

