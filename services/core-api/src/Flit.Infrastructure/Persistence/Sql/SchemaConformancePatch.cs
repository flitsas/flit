namespace Flit.Infrastructure.Persistence.Sql;

/// <summary>
/// Parche idempotente: PK pk_, RLS faltante, triggers y comentarios PII (checklist db-schema-validator).
/// </summary>
internal static class SchemaConformancePatchSql
{
    public const string Down = """
        DROP POLICY IF EXISTS tenant_isolation ON admin.tenant_config_audit_logs;
        ALTER TABLE admin.tenant_config_audit_logs DISABLE ROW LEVEL SECURITY;
        DROP POLICY IF EXISTS tenant_isolation ON tramites.business_rules;
        ALTER TABLE tramites.business_rules DISABLE ROW LEVEL SECURITY;
        """;
}
