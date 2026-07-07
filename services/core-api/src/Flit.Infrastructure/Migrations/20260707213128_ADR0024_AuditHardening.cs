using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// ADR-0024 (RNF01) — auditoría mínima de configuración: columnas aditivas nullable
    /// (client_ip, result, operation, error_code) + índice por resultado en
    /// admin.tenant_config_audit_logs. El DDL embebido (24-ADR0024-audit-hardening.sql) es
    /// idempotente (ADD COLUMN/INDEX IF NOT EXISTS); el snapshot/Designer reconcilian el modelo
    /// EF. Mismo patrón que HU10545_OtRequirements / HU10588_RuesProviderBinding.
    /// </remarks>
    public partial class ADR0024_AuditHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("24-ADR0024-audit-hardening.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tenant_config_audit_logs_tenant_id_result_changed_at",
                schema: "admin",
                table: "tenant_config_audit_logs");

            migrationBuilder.DropColumn(
                name: "client_ip",
                schema: "admin",
                table: "tenant_config_audit_logs");

            migrationBuilder.DropColumn(
                name: "error_code",
                schema: "admin",
                table: "tenant_config_audit_logs");

            migrationBuilder.DropColumn(
                name: "operation",
                schema: "admin",
                table: "tenant_config_audit_logs");

            migrationBuilder.DropColumn(
                name: "result",
                schema: "admin",
                table: "tenant_config_audit_logs");
        }
    }
}
