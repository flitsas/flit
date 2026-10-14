using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Feature #13508 (Épica #13272) — tablas globales <c>admin.ai_tool_provider_settings</c>,
/// <c>admin.ai_provider_transitions</c> y <c>admin.ai_failover_alerts</c> (ADR-0070 Propuesto).
/// </summary>
/// <remarks>
/// Sin <c>tenant_id</c>/RLS a propósito (config de plataforma Super Admin). Secretos solo en env.
/// DDL: <c>Persistence/Sql/Ddl/130-Feature13508-ai-tool-provider-settings.sql</c>.
/// </remarks>
[DbContext(typeof(FlitDbContext))]
[Migration("20261012200000_Feature13508_AiToolProviderSettings")]
public partial class Feature13508_AiToolProviderSettings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("130-Feature13508-ai-tool-provider-settings.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS admin.ai_failover_alerts;
            DROP TRIGGER IF EXISTS tr_ai_provider_transitions_immutable ON admin.ai_provider_transitions;
            DROP TABLE IF EXISTS admin.ai_provider_transitions;
            DROP FUNCTION IF EXISTS admin.trg_ai_provider_transitions_immutable();
            DROP TRIGGER IF EXISTS tr_ai_tool_provider_settings_audit ON admin.ai_tool_provider_settings;
            DROP TRIGGER IF EXISTS tr_ai_tool_provider_settings_row_version ON admin.ai_tool_provider_settings;
            DROP TABLE IF EXISTS admin.ai_tool_provider_settings;
            """);
    }
}
