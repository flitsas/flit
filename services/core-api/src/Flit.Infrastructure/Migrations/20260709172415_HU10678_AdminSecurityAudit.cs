using System;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10678 — generaliza admin.tenant_config_audit_logs (HU #10190/ADR-0024) a rastro
    /// único de auditoría administrativa/seguridad: tenant_id nullable + columnas aditivas
    /// (tenant_type, module, target_entity_type, target_entity_id, user_agent) + índices para
    /// las consultas globales del SuperAdmin + RLS con bypass SuperAdmin/filas sin tenant. El
    /// DDL embebido (32-HU10678-admin-security-audit.sql) es idempotente (ADD COLUMN/INDEX IF
    /// NOT EXISTS, DROP POLICY IF EXISTS); el snapshot/Designer reconcilian el modelo EF.
    /// Mismo patrón que ADR0024_AuditHardening.
    /// </remarks>
    public partial class HU10678_AdminSecurityAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("32-HU10678-admin-security-audit.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revierte la policy RLS al predicado original (solo tenant actual, sin bypass
            // SuperAdmin ni filas sin tenant) — mismo texto que 11-schema-conformance-patch.sql.
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS tenant_isolation ON admin.tenant_config_audit_logs;
                CREATE POLICY tenant_isolation ON admin.tenant_config_audit_logs
                  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
                """);

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_changed_at",
                schema: "admin",
                table: "tenant_config_audit_logs");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_changed_by_changed_at",
                schema: "admin",
                table: "tenant_config_audit_logs");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_module_changed_at",
                schema: "admin",
                table: "tenant_config_audit_logs");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_target_changed_at",
                schema: "admin",
                table: "tenant_config_audit_logs");

            migrationBuilder.DropColumn(
                name: "module",
                schema: "admin",
                table: "tenant_config_audit_logs");

            migrationBuilder.DropColumn(
                name: "target_entity_id",
                schema: "admin",
                table: "tenant_config_audit_logs");

            migrationBuilder.DropColumn(
                name: "target_entity_type",
                schema: "admin",
                table: "tenant_config_audit_logs");

            migrationBuilder.DropColumn(
                name: "tenant_type",
                schema: "admin",
                table: "tenant_config_audit_logs");

            migrationBuilder.DropColumn(
                name: "user_agent",
                schema: "admin",
                table: "tenant_config_audit_logs");

            migrationBuilder.AlterColumn<Guid>(
                name: "tenant_id",
                schema: "admin",
                table: "tenant_config_audit_logs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
