-- HU schema conformance patch (PK pk_, RLS, triggers, PII)

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'identity' AND r.relname = 'tenants' AND c.conname = 'tenants_pkey'
  ) THEN
    ALTER TABLE identity.tenants RENAME CONSTRAINT tenants_pkey TO pk_tenants;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'identity' AND r.relname = 'users' AND c.conname = 'users_pkey'
  ) THEN
    ALTER TABLE identity.users RENAME CONSTRAINT users_pkey TO pk_users;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'security' AND r.relname = 'user_credentials' AND c.conname = 'user_credentials_pkey'
  ) THEN
    ALTER TABLE security.user_credentials RENAME CONSTRAINT user_credentials_pkey TO pk_user_credentials;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'security' AND r.relname = 'password_reset_tokens' AND c.conname = 'password_reset_tokens_pkey'
  ) THEN
    ALTER TABLE security.password_reset_tokens RENAME CONSTRAINT password_reset_tokens_pkey TO pk_password_reset_tokens;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'security' AND r.relname = 'user_temp_suspensions' AND c.conname = 'user_temp_suspensions_pkey'
  ) THEN
    ALTER TABLE security.user_temp_suspensions RENAME CONSTRAINT user_temp_suspensions_pkey TO pk_user_temp_suspensions;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'security' AND r.relname = 'modules' AND c.conname = 'modules_pkey'
  ) THEN
    ALTER TABLE security.modules RENAME CONSTRAINT modules_pkey TO pk_modules;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'security' AND r.relname = 'permissions' AND c.conname = 'permissions_pkey'
  ) THEN
    ALTER TABLE security.permissions RENAME CONSTRAINT permissions_pkey TO pk_permissions;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'security' AND r.relname = 'roles' AND c.conname = 'roles_pkey'
  ) THEN
    ALTER TABLE security.roles RENAME CONSTRAINT roles_pkey TO pk_roles;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'security' AND r.relname = 'role_permissions' AND c.conname = 'role_permissions_pkey'
  ) THEN
    ALTER TABLE security.role_permissions RENAME CONSTRAINT role_permissions_pkey TO pk_role_permissions;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'security' AND r.relname = 'user_role_assignments' AND c.conname = 'user_role_assignments_pkey'
  ) THEN
    ALTER TABLE security.user_role_assignments RENAME CONSTRAINT user_role_assignments_pkey TO pk_user_role_assignments;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'security' AND r.relname = 'user_invitations' AND c.conname = 'user_invitations_pkey'
  ) THEN
    ALTER TABLE security.user_invitations RENAME CONSTRAINT user_invitations_pkey TO pk_user_invitations;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'tramites' AND r.relname = 'procedure_types' AND c.conname = 'procedure_types_pkey'
  ) THEN
    ALTER TABLE tramites.procedure_types RENAME CONSTRAINT procedure_types_pkey TO pk_procedure_types;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'tramites' AND r.relname = 'procedure_entities' AND c.conname = 'procedure_entities_pkey'
  ) THEN
    ALTER TABLE tramites.procedure_entities RENAME CONSTRAINT procedure_entities_pkey TO pk_procedure_entities;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'tramites' AND r.relname = 'conformation_rules' AND c.conname = 'conformation_rules_pkey'
  ) THEN
    ALTER TABLE tramites.conformation_rules RENAME CONSTRAINT conformation_rules_pkey TO pk_conformation_rules;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'tramites' AND r.relname = 'procedure_steps' AND c.conname = 'procedure_steps_pkey'
  ) THEN
    ALTER TABLE tramites.procedure_steps RENAME CONSTRAINT procedure_steps_pkey TO pk_procedure_steps;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'tramites' AND r.relname = 'procedure_sections' AND c.conname = 'procedure_sections_pkey'
  ) THEN
    ALTER TABLE tramites.procedure_sections RENAME CONSTRAINT procedure_sections_pkey TO pk_procedure_sections;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'tramites' AND r.relname = 'form_fields' AND c.conname = 'form_fields_pkey'
  ) THEN
    ALTER TABLE tramites.form_fields RENAME CONSTRAINT form_fields_pkey TO pk_form_fields;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'tramites' AND r.relname = 'external_data_sources' AND c.conname = 'external_data_sources_pkey'
  ) THEN
    ALTER TABLE tramites.external_data_sources RENAME CONSTRAINT external_data_sources_pkey TO pk_external_data_sources;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'tramites' AND r.relname = 'field_api_bindings' AND c.conname = 'field_api_bindings_pkey'
  ) THEN
    ALTER TABLE tramites.field_api_bindings RENAME CONSTRAINT field_api_bindings_pkey TO pk_field_api_bindings;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'tramites' AND r.relname = 'business_rules' AND c.conname = 'business_rules_pkey'
  ) THEN
    ALTER TABLE tramites.business_rules RENAME CONSTRAINT business_rules_pkey TO pk_business_rules;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'tramites' AND r.relname = 'business_rule_procedure_types' AND c.conname = 'business_rule_procedure_types_pkey'
  ) THEN
    ALTER TABLE tramites.business_rule_procedure_types RENAME CONSTRAINT business_rule_procedure_types_pkey TO pk_business_rule_procedure_types;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'tramites' AND r.relname = 'rule_condition_groups' AND c.conname = 'rule_condition_groups_pkey'
  ) THEN
    ALTER TABLE tramites.rule_condition_groups RENAME CONSTRAINT rule_condition_groups_pkey TO pk_rule_condition_groups;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'tramites' AND r.relname = 'rule_conditions' AND c.conname = 'rule_conditions_pkey'
  ) THEN
    ALTER TABLE tramites.rule_conditions RENAME CONSTRAINT rule_conditions_pkey TO pk_rule_conditions;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'tramites' AND r.relname = 'rule_actions' AND c.conname = 'rule_actions_pkey'
  ) THEN
    ALTER TABLE tramites.rule_actions RENAME CONSTRAINT rule_actions_pkey TO pk_rule_actions;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'tramites' AND r.relname = 'api_endpoint_catalog' AND c.conname = 'api_endpoint_catalog_pkey'
  ) THEN
    ALTER TABLE tramites.api_endpoint_catalog RENAME CONSTRAINT api_endpoint_catalog_pkey TO pk_api_endpoint_catalog;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'tramites' AND r.relname = 'procedure_instances' AND c.conname = 'procedure_instances_pkey'
  ) THEN
    ALTER TABLE tramites.procedure_instances RENAME CONSTRAINT procedure_instances_pkey TO pk_procedure_instances;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'tramites' AND r.relname = 'procedure_instance_actors' AND c.conname = 'procedure_instance_actors_pkey'
  ) THEN
    ALTER TABLE tramites.procedure_instance_actors RENAME CONSTRAINT procedure_instance_actors_pkey TO pk_procedure_instance_actors;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'tramites' AND r.relname = 'procedure_instance_field_values' AND c.conname = 'procedure_instance_field_values_pkey'
  ) THEN
    ALTER TABLE tramites.procedure_instance_field_values RENAME CONSTRAINT procedure_instance_field_values_pkey TO pk_procedure_instance_field_values;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'tramites' AND r.relname = 'procedure_instance_status_history' AND c.conname = 'procedure_instance_status_history_pkey'
  ) THEN
    ALTER TABLE tramites.procedure_instance_status_history RENAME CONSTRAINT procedure_instance_status_history_pkey TO pk_procedure_instance_status_history;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'admin' AND r.relname = 'tenant_profiles' AND c.conname = 'tenant_profiles_pkey'
  ) THEN
    ALTER TABLE admin.tenant_profiles RENAME CONSTRAINT tenant_profiles_pkey TO pk_tenant_profiles;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'admin' AND r.relname = 'tenant_operational_policies' AND c.conname = 'tenant_operational_policies_pkey'
  ) THEN
    ALTER TABLE admin.tenant_operational_policies RENAME CONSTRAINT tenant_operational_policies_pkey TO pk_tenant_operational_policies;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'admin' AND r.relname = 'tenant_whitelist_users' AND c.conname = 'tenant_whitelist_users_pkey'
  ) THEN
    ALTER TABLE admin.tenant_whitelist_users RENAME CONSTRAINT tenant_whitelist_users_pkey TO pk_tenant_whitelist_users;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'admin' AND r.relname = 'tenant_transit_office_grants' AND c.conname = 'tenant_transit_office_grants_pkey'
  ) THEN
    ALTER TABLE admin.tenant_transit_office_grants RENAME CONSTRAINT tenant_transit_office_grants_pkey TO pk_tenant_transit_office_grants;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'admin' AND r.relname = 'tenant_config_audit_logs' AND c.conname = 'tenant_config_audit_logs_pkey'
  ) THEN
    ALTER TABLE admin.tenant_config_audit_logs RENAME CONSTRAINT tenant_config_audit_logs_pkey TO pk_tenant_config_audit_logs;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'tramites' AND r.relname = 'document_types' AND c.conname = 'document_types_pkey'
  ) THEN
    ALTER TABLE tramites.document_types RENAME CONSTRAINT document_types_pkey TO pk_document_types;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'tramites' AND r.relname = 'procedure_document_requirements' AND c.conname = 'procedure_document_requirements_pkey'
  ) THEN
    ALTER TABLE tramites.procedure_document_requirements RENAME CONSTRAINT procedure_document_requirements_pkey TO pk_procedure_document_requirements;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'tramites' AND r.relname = 'document_order_overrides' AND c.conname = 'document_order_overrides_pkey'
  ) THEN
    ALTER TABLE tramites.document_order_overrides RENAME CONSTRAINT document_order_overrides_pkey TO pk_document_order_overrides;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'tramites' AND r.relname = 'procedure_document_snapshots' AND c.conname = 'procedure_document_snapshots_pkey'
  ) THEN
    ALTER TABLE tramites.procedure_document_snapshots RENAME CONSTRAINT procedure_document_snapshots_pkey TO pk_procedure_document_snapshots;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'catalogs' AND r.relname = 'transit_offices' AND c.conname = 'transit_offices_pkey'
  ) THEN
    ALTER TABLE catalogs.transit_offices RENAME CONSTRAINT transit_offices_pkey TO pk_transit_offices;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'admin' AND r.relname = 'transit_office_profiles' AND c.conname = 'transit_office_profiles_pkey'
  ) THEN
    ALTER TABLE admin.transit_office_profiles RENAME CONSTRAINT transit_office_profiles_pkey TO pk_transit_office_profiles;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'admin' AND r.relname = 'ot_feature_flags' AND c.conname = 'ot_feature_flags_pkey'
  ) THEN
    ALTER TABLE admin.ot_feature_flags RENAME CONSTRAINT ot_feature_flags_pkey TO pk_ot_feature_flags;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'admin' AND r.relname = 'ot_webhook_subscriptions' AND c.conname = 'ot_webhook_subscriptions_pkey'
  ) THEN
    ALTER TABLE admin.ot_webhook_subscriptions RENAME CONSTRAINT ot_webhook_subscriptions_pkey TO pk_ot_webhook_subscriptions;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'admin' AND r.relname = 'ot_api_call_logs' AND c.conname = 'ot_api_call_logs_pkey'
  ) THEN
    ALTER TABLE admin.ot_api_call_logs RENAME CONSTRAINT ot_api_call_logs_pkey TO pk_ot_api_call_logs;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'admin' AND r.relname = 'ot_document_precedence' AND c.conname = 'ot_document_precedence_pkey'
  ) THEN
    ALTER TABLE admin.ot_document_precedence RENAME CONSTRAINT ot_document_precedence_pkey TO pk_ot_document_precedence;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'admin' AND r.relname = 'ot_document_tags' AND c.conname = 'ot_document_tags_pkey'
  ) THEN
    ALTER TABLE admin.ot_document_tags RENAME CONSTRAINT ot_document_tags_pkey TO pk_ot_document_tags;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'analytics' AND r.relname = 'procedure_metrics_daily' AND c.conname = 'procedure_metrics_daily_pkey'
  ) THEN
    ALTER TABLE analytics.procedure_metrics_daily RENAME CONSTRAINT procedure_metrics_daily_pkey TO pk_procedure_metrics_daily;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'analytics' AND r.relname = 'user_productivity_daily' AND c.conname = 'user_productivity_daily_pkey'
  ) THEN
    ALTER TABLE analytics.user_productivity_daily RENAME CONSTRAINT user_productivity_daily_pkey TO pk_user_productivity_daily;
  END IF;
END $$;

DO $$ BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_constraint c
    JOIN pg_class r ON r.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = r.relnamespace
    WHERE n.nspname = 'audit' AND r.relname = 'audit_logs' AND c.conname = 'audit_logs_pkey'
  ) THEN
    ALTER TABLE audit.audit_logs RENAME CONSTRAINT audit_logs_pkey TO pk_audit_logs;
  END IF;
END $$;

ALTER TABLE tramites.business_rules ALTER COLUMN tenant_id SET NOT NULL;

ALTER TABLE tramites.business_rules ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON tramites.business_rules;
CREATE POLICY tenant_isolation ON tramites.business_rules
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

ALTER TABLE admin.tenant_config_audit_logs ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON admin.tenant_config_audit_logs;
CREATE POLICY tenant_isolation ON admin.tenant_config_audit_logs
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

-- Triggers negocio (checklist A16)
DROP TRIGGER IF EXISTS tr_tenant_operational_policies_row_version ON admin.tenant_operational_policies;
CREATE TRIGGER tr_tenant_operational_policies_row_version BEFORE UPDATE ON admin.tenant_operational_policies
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_tenant_operational_policies_audit ON admin.tenant_operational_policies;
CREATE TRIGGER tr_tenant_operational_policies_audit AFTER INSERT OR UPDATE OR DELETE ON admin.tenant_operational_policies
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_tenant_profiles_row_version ON admin.tenant_profiles;
CREATE TRIGGER tr_tenant_profiles_row_version BEFORE UPDATE ON admin.tenant_profiles
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_tenant_profiles_audit ON admin.tenant_profiles;
CREATE TRIGGER tr_tenant_profiles_audit AFTER INSERT OR UPDATE OR DELETE ON admin.tenant_profiles
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_transit_office_profiles_row_version ON admin.transit_office_profiles;
CREATE TRIGGER tr_transit_office_profiles_row_version BEFORE UPDATE ON admin.transit_office_profiles
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_transit_office_profiles_audit ON admin.transit_office_profiles;
CREATE TRIGGER tr_transit_office_profiles_audit AFTER INSERT OR UPDATE OR DELETE ON admin.transit_office_profiles
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_tenants_row_version ON identity.tenants;
CREATE TRIGGER tr_tenants_row_version BEFORE UPDATE ON identity.tenants
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_tenants_audit ON identity.tenants;
CREATE TRIGGER tr_tenants_audit AFTER INSERT OR UPDATE OR DELETE ON identity.tenants
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_users_row_version ON identity.users;
CREATE TRIGGER tr_users_row_version BEFORE UPDATE ON identity.users
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_users_audit ON identity.users;
CREATE TRIGGER tr_users_audit AFTER INSERT OR UPDATE OR DELETE ON identity.users
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_roles_row_version ON security.roles;
CREATE TRIGGER tr_roles_row_version BEFORE UPDATE ON security.roles
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_roles_audit ON security.roles;
CREATE TRIGGER tr_roles_audit AFTER INSERT OR UPDATE OR DELETE ON security.roles
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_user_credentials_row_version ON security.user_credentials;
CREATE TRIGGER tr_user_credentials_row_version BEFORE UPDATE ON security.user_credentials
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_user_credentials_audit ON security.user_credentials;
CREATE TRIGGER tr_user_credentials_audit AFTER INSERT OR UPDATE OR DELETE ON security.user_credentials
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_user_invitations_row_version ON security.user_invitations;
CREATE TRIGGER tr_user_invitations_row_version BEFORE UPDATE ON security.user_invitations
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_user_invitations_audit ON security.user_invitations;
CREATE TRIGGER tr_user_invitations_audit AFTER INSERT OR UPDATE OR DELETE ON security.user_invitations
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_user_role_assignments_row_version ON security.user_role_assignments;
CREATE TRIGGER tr_user_role_assignments_row_version BEFORE UPDATE ON security.user_role_assignments
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_user_role_assignments_audit ON security.user_role_assignments;
CREATE TRIGGER tr_user_role_assignments_audit AFTER INSERT OR UPDATE OR DELETE ON security.user_role_assignments
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_user_temp_suspensions_row_version ON security.user_temp_suspensions;
CREATE TRIGGER tr_user_temp_suspensions_row_version BEFORE UPDATE ON security.user_temp_suspensions
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_user_temp_suspensions_audit ON security.user_temp_suspensions;
CREATE TRIGGER tr_user_temp_suspensions_audit AFTER INSERT OR UPDATE OR DELETE ON security.user_temp_suspensions
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_business_rules_row_version ON tramites.business_rules;
CREATE TRIGGER tr_business_rules_row_version BEFORE UPDATE ON tramites.business_rules
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_business_rules_audit ON tramites.business_rules;
CREATE TRIGGER tr_business_rules_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.business_rules
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_procedure_instances_row_version ON tramites.procedure_instances;
CREATE TRIGGER tr_procedure_instances_row_version BEFORE UPDATE ON tramites.procedure_instances
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_procedure_instances_audit ON tramites.procedure_instances;
CREATE TRIGGER tr_procedure_instances_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_instances
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_ot_api_call_logs_audit ON admin.ot_api_call_logs;
CREATE TRIGGER tr_ot_api_call_logs_audit AFTER INSERT OR UPDATE OR DELETE ON admin.ot_api_call_logs
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_ot_document_precedence_audit ON admin.ot_document_precedence;
CREATE TRIGGER tr_ot_document_precedence_audit AFTER INSERT OR UPDATE OR DELETE ON admin.ot_document_precedence
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_ot_document_tags_audit ON admin.ot_document_tags;
CREATE TRIGGER tr_ot_document_tags_audit AFTER INSERT OR UPDATE OR DELETE ON admin.ot_document_tags
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_ot_feature_flags_audit ON admin.ot_feature_flags;
CREATE TRIGGER tr_ot_feature_flags_audit AFTER INSERT OR UPDATE OR DELETE ON admin.ot_feature_flags
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_ot_webhook_subscriptions_audit ON admin.ot_webhook_subscriptions;
CREATE TRIGGER tr_ot_webhook_subscriptions_audit AFTER INSERT OR UPDATE OR DELETE ON admin.ot_webhook_subscriptions
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_tenant_config_audit_logs_audit ON admin.tenant_config_audit_logs;
CREATE TRIGGER tr_tenant_config_audit_logs_audit AFTER INSERT OR UPDATE OR DELETE ON admin.tenant_config_audit_logs
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_tenant_transit_office_grants_audit ON admin.tenant_transit_office_grants;
CREATE TRIGGER tr_tenant_transit_office_grants_audit AFTER INSERT OR UPDATE OR DELETE ON admin.tenant_transit_office_grants
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_tenant_whitelist_users_audit ON admin.tenant_whitelist_users;
CREATE TRIGGER tr_tenant_whitelist_users_audit AFTER INSERT OR UPDATE OR DELETE ON admin.tenant_whitelist_users
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_procedure_metrics_daily_audit ON analytics.procedure_metrics_daily;
CREATE TRIGGER tr_procedure_metrics_daily_audit AFTER INSERT OR UPDATE OR DELETE ON analytics.procedure_metrics_daily
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_user_productivity_daily_audit ON analytics.user_productivity_daily;
CREATE TRIGGER tr_user_productivity_daily_audit AFTER INSERT OR UPDATE OR DELETE ON analytics.user_productivity_daily
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_password_reset_tokens_audit ON security.password_reset_tokens;
CREATE TRIGGER tr_password_reset_tokens_audit AFTER INSERT OR UPDATE OR DELETE ON security.password_reset_tokens
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_role_permissions_audit ON security.role_permissions;
CREATE TRIGGER tr_role_permissions_audit AFTER INSERT OR UPDATE OR DELETE ON security.role_permissions
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_procedure_document_snapshots_audit ON tramites.procedure_document_snapshots;
CREATE TRIGGER tr_procedure_document_snapshots_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_document_snapshots
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_procedure_instance_actors_audit ON tramites.procedure_instance_actors;
CREATE TRIGGER tr_procedure_instance_actors_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_instance_actors
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_procedure_instance_field_values_audit ON tramites.procedure_instance_field_values;
CREATE TRIGGER tr_procedure_instance_field_values_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_instance_field_values
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_procedure_instance_status_history_audit ON tramites.procedure_instance_status_history;
CREATE TRIGGER tr_procedure_instance_status_history_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_instance_status_history
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

-- PII (checklist A15)
COMMENT ON COLUMN identity.tenants.tax_id IS '@pii:medium';
COMMENT ON COLUMN identity.users.email IS '@pii:high';
COMMENT ON COLUMN identity.users.display_name IS '@pii:medium';
COMMENT ON COLUMN security.user_credentials.password_hash IS '@pii:high';
COMMENT ON COLUMN security.password_reset_tokens.token_hash IS '@pii:high';
COMMENT ON COLUMN security.user_invitations.email IS '@pii:high';
COMMENT ON COLUMN security.user_invitations.token_hash IS '@pii:high';
COMMENT ON COLUMN tramites.procedure_instance_actors.document_number IS '@pii:high';
COMMENT ON COLUMN tramites.procedure_instance_actors.email IS '@pii:high';
COMMENT ON COLUMN tramites.procedure_instance_actors.full_name IS '@pii:medium';
COMMENT ON COLUMN tramites.procedure_instance_actors.phone IS '@pii:medium';
COMMENT ON COLUMN tramites.procedure_instance_field_values.value_text IS '@pii:medium';
COMMENT ON COLUMN admin.tenant_profiles.contact_email IS '@pii:high';
COMMENT ON COLUMN admin.tenant_profiles.contact_phone IS '@pii:medium';
COMMENT ON COLUMN admin.tenant_profiles.address IS '@pii:medium';
COMMENT ON COLUMN admin.tenant_whitelist_users.email IS '@pii:high';
COMMENT ON COLUMN admin.ot_webhook_subscriptions.secret_hash IS '@pii:high';