namespace Flit.Infrastructure.Persistence.Sql;

/// <summary>
/// Rollback SQL for phase-1 DDL migrations (HU #10148–#10153).
/// </summary>
internal static class Phase1DdlDown
{
    public const string Hu10146 = """
        DROP TABLE IF EXISTS security.user_temp_suspensions CASCADE;
        DROP TABLE IF EXISTS security.password_reset_tokens CASCADE;
        DROP TABLE IF EXISTS security.user_credentials CASCADE;
        DROP TABLE IF EXISTS identity.users CASCADE;
        DROP TABLE IF EXISTS identity.tenants CASCADE;
        """;

    public const string Hu10148 = """
        DROP TABLE IF EXISTS security.user_role_assignments CASCADE;
        DROP TABLE IF EXISTS security.role_permissions CASCADE;
        DROP TABLE IF EXISTS security.roles CASCADE;
        DROP TABLE IF EXISTS security.permissions CASCADE;
        DROP TABLE IF EXISTS security.modules CASCADE;
        """;

    public const string Hu10147 = """
        DROP TABLE IF EXISTS security.user_invitations CASCADE;
        """;

    public const string Hu10151 = """
        DROP TABLE IF EXISTS tramites.field_api_bindings CASCADE;
        DROP TABLE IF EXISTS tramites.external_data_sources CASCADE;
        DROP TABLE IF EXISTS tramites.form_fields CASCADE;
        DROP TABLE IF EXISTS tramites.procedure_sections CASCADE;
        DROP TABLE IF EXISTS tramites.procedure_steps CASCADE;
        DROP TABLE IF EXISTS tramites.conformation_rules CASCADE;
        DROP TABLE IF EXISTS tramites.procedure_entities CASCADE;
        DROP TABLE IF EXISTS tramites.procedure_types CASCADE;
        """;

    public const string Hu10149 = """
        DROP TABLE IF EXISTS tramites.api_endpoint_catalog CASCADE;
        DROP TABLE IF EXISTS tramites.rule_actions CASCADE;
        DROP TABLE IF EXISTS tramites.rule_conditions CASCADE;
        DROP TABLE IF EXISTS tramites.rule_condition_groups CASCADE;
        DROP TABLE IF EXISTS tramites.business_rule_procedure_types CASCADE;
        DROP TABLE IF EXISTS tramites.business_rules CASCADE;
        """;

    public const string Hu10150 = """
        DROP TABLE IF EXISTS tramites.procedure_instance_status_history CASCADE;
        DROP TABLE IF EXISTS tramites.procedure_instance_field_values CASCADE;
        DROP TABLE IF EXISTS tramites.procedure_instance_actors CASCADE;
        DROP TABLE IF EXISTS tramites.procedure_instances CASCADE;
        """;

    public const string Hu10154 = """
        DROP TABLE IF EXISTS admin.tenant_config_audit_logs CASCADE;
        DROP TABLE IF EXISTS admin.tenant_transit_office_grants CASCADE;
        DROP TABLE IF EXISTS admin.tenant_whitelist_users CASCADE;
        DROP TABLE IF EXISTS admin.tenant_operational_policies CASCADE;
        DROP TABLE IF EXISTS admin.tenant_profiles CASCADE;
        """;

    public const string Hu10155 = """
        DROP TABLE IF EXISTS tramites.procedure_document_snapshots CASCADE;
        DROP TABLE IF EXISTS tramites.document_order_overrides CASCADE;
        DROP TABLE IF EXISTS tramites.procedure_document_requirements CASCADE;
        DROP TABLE IF EXISTS tramites.document_types CASCADE;
        """;

    public const string Hu10152 = """
        DROP TABLE IF EXISTS admin.ot_document_tags CASCADE;
        DROP TABLE IF EXISTS admin.ot_document_precedence CASCADE;
        DROP TABLE IF EXISTS admin.ot_api_call_logs CASCADE;
        DROP TABLE IF EXISTS admin.ot_webhook_subscriptions CASCADE;
        DROP TABLE IF EXISTS admin.ot_feature_flags CASCADE;
        DROP TABLE IF EXISTS admin.transit_office_profiles CASCADE;
        DROP TABLE IF EXISTS catalogs.transit_offices CASCADE;
        """;

    public const string Hu10153 = """
        DROP TABLE IF EXISTS analytics.user_productivity_daily CASCADE;
        DROP TABLE IF EXISTS analytics.procedure_metrics_daily CASCADE;
        """;

    /// <summary>
    /// Rollback de HU #10151 (Revisión) — DDL incremental.
    /// Revierte: form_fields (G3), procedure_types (G2), procedure_steps/sections (A16),
    /// consultation_templates y triggers. NO toca el DDL original (Hu10151).
    /// AC5: reversible sin afectar la migración base 20260617230200.
    /// </summary>
    public const string Hu10151Revision = """
        -- G3: Revertir columnas de form_fields
        ALTER TABLE tramites.form_fields
            DROP CONSTRAINT IF EXISTS fk_form_fields_consultation_templates;
        DROP INDEX IF EXISTS tramites.ix_form_fields_consultation_template_id;
        ALTER TABLE tramites.form_fields
            DROP COLUMN IF EXISTS is_locked,
            DROP COLUMN IF EXISTS lock_reason,
            DROP COLUMN IF EXISTS consultation_template_id,
            DROP COLUMN IF EXISTS row_version;
        DROP TRIGGER IF EXISTS tr_form_fields_row_version ON tramites.form_fields;
        DROP TRIGGER IF EXISTS tr_form_fields_audit ON tramites.form_fields;

        -- A16: Revertir triggers de procedure_sections
        DROP TRIGGER IF EXISTS tr_procedure_sections_row_version ON tramites.procedure_sections;
        DROP TRIGGER IF EXISTS tr_procedure_sections_audit ON tramites.procedure_sections;
        ALTER TABLE tramites.procedure_sections DROP COLUMN IF EXISTS row_version;

        -- A16: Revertir triggers de procedure_steps
        DROP TRIGGER IF EXISTS tr_procedure_steps_row_version ON tramites.procedure_steps;
        DROP TRIGGER IF EXISTS tr_procedure_steps_audit ON tramites.procedure_steps;
        ALTER TABLE tramites.procedure_steps DROP COLUMN IF EXISTS row_version;

        -- G2: Revertir columnas de procedure_types
        ALTER TABLE tramites.procedure_types
            DROP CONSTRAINT IF EXISTS ck_procedure_types_publication_status;
        DROP TRIGGER IF EXISTS tr_procedure_types_row_version ON tramites.procedure_types;
        DROP TRIGGER IF EXISTS tr_procedure_types_audit ON tramites.procedure_types;
        ALTER TABLE tramites.procedure_types
            DROP COLUMN IF EXISTS publication_status,
            DROP COLUMN IF EXISTS published_at,
            DROP COLUMN IF EXISTS published_by,
            DROP COLUMN IF EXISTS row_version;

        -- A16: Revertir trigger de external_data_sources
        DROP TRIGGER IF EXISTS tr_external_data_sources_audit ON tramites.external_data_sources;

        -- G1: Eliminar tabla consultation_templates (y sus triggers/índices)
        DROP TABLE IF EXISTS tramites.consultation_templates CASCADE;
        """;
}
