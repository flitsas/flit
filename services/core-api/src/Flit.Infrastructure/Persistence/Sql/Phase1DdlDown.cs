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
}
