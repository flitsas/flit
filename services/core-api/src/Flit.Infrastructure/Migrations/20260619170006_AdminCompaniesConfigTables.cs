using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdminCompaniesConfigTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "admin");

            migrationBuilder.CreateTable(
                name: "tenant_config_audit_logs",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    field_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    old_value = table.Column<string>(type: "jsonb", nullable: true),
                    new_value = table.Column<string>(type: "jsonb", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_config_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_operational_policies",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    allow_initial_registration = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    allow_misc_new_vehicles = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    only_own_vehicles = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    signature_vault_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    notification_channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "flit_smtp"),
                    notification_target = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "submitter"),
                    payment_methods = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'"),
                    runt_provider_strategy = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "verifik"),
                    runt_failover_timeout_ms = table.Column<int>(type: "integer", nullable: false, defaultValue: 4000),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_operational_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_transit_office_grants",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transit_office_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_transit_office_grants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_whitelist_users",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    added_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_whitelist_users", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_config_audit_logs_tenant_id_changed_at",
                schema: "admin",
                table: "tenant_config_audit_logs",
                columns: new[] { "tenant_id", "changed_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "uq_tenant_operational_policies_tenant_id",
                schema: "admin",
                table: "tenant_operational_policies",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_transit_office_grants_tenant_id",
                schema: "admin",
                table: "tenant_transit_office_grants",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "uq_tenant_transit_office_grants",
                schema: "admin",
                table: "tenant_transit_office_grants",
                columns: new[] { "tenant_id", "transit_office_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_whitelist_users_tenant_id",
                schema: "admin",
                table: "tenant_whitelist_users",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "uq_tenant_whitelist_users_tenant_email",
                schema: "admin",
                table: "tenant_whitelist_users",
                columns: new[] { "tenant_id", "email" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_config_audit_logs",
                schema: "admin");

            migrationBuilder.DropTable(
                name: "tenant_operational_policies",
                schema: "admin");

            migrationBuilder.DropTable(
                name: "tenant_transit_office_grants",
                schema: "admin");

            migrationBuilder.DropTable(
                name: "tenant_whitelist_users",
                schema: "admin");
        }
    }
}
