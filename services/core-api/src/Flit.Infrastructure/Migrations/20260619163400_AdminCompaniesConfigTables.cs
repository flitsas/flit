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
            migrationBuilder.DropPrimaryKey(
                name: "PK_users",
                schema: "identity",
                table: "users");

            migrationBuilder.DropPrimaryKey(
                name: "PK_user_temp_suspensions",
                schema: "security",
                table: "user_temp_suspensions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_user_credentials",
                schema: "security",
                table: "user_credentials");

            migrationBuilder.DropPrimaryKey(
                name: "PK_tenants",
                schema: "identity",
                table: "tenants");

            migrationBuilder.DropPrimaryKey(
                name: "PK_password_reset_tokens",
                schema: "security",
                table: "password_reset_tokens");

            migrationBuilder.EnsureSchema(
                name: "admin");

            migrationBuilder.RenameColumn(
                name: "Status",
                schema: "identity",
                table: "users",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Email",
                schema: "identity",
                table: "users",
                newName: "email");

            migrationBuilder.RenameColumn(
                name: "Id",
                schema: "identity",
                table: "users",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedBy",
                schema: "identity",
                table: "users",
                newName: "updated_by");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                schema: "identity",
                table: "users",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "RowVersion",
                schema: "identity",
                table: "users",
                newName: "row_version");

            migrationBuilder.RenameColumn(
                name: "DisplayName",
                schema: "identity",
                table: "users",
                newName: "display_name");

            migrationBuilder.RenameColumn(
                name: "DeletedBy",
                schema: "identity",
                table: "users",
                newName: "deleted_by");

            migrationBuilder.RenameColumn(
                name: "DeletedAt",
                schema: "identity",
                table: "users",
                newName: "deleted_at");

            migrationBuilder.RenameColumn(
                name: "CreatedBy",
                schema: "identity",
                table: "users",
                newName: "created_by");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                schema: "identity",
                table: "users",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "Reason",
                schema: "security",
                table: "user_temp_suspensions",
                newName: "reason");

            migrationBuilder.RenameColumn(
                name: "Id",
                schema: "security",
                table: "user_temp_suspensions",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                schema: "security",
                table: "user_temp_suspensions",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "UpdatedBy",
                schema: "security",
                table: "user_temp_suspensions",
                newName: "updated_by");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                schema: "security",
                table: "user_temp_suspensions",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                schema: "security",
                table: "user_temp_suspensions",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "StartsAt",
                schema: "security",
                table: "user_temp_suspensions",
                newName: "starts_at");

            migrationBuilder.RenameColumn(
                name: "RowVersion",
                schema: "security",
                table: "user_temp_suspensions",
                newName: "row_version");

            migrationBuilder.RenameColumn(
                name: "EndsAt",
                schema: "security",
                table: "user_temp_suspensions",
                newName: "ends_at");

            migrationBuilder.RenameColumn(
                name: "DeletedBy",
                schema: "security",
                table: "user_temp_suspensions",
                newName: "deleted_by");

            migrationBuilder.RenameColumn(
                name: "DeletedAt",
                schema: "security",
                table: "user_temp_suspensions",
                newName: "deleted_at");

            migrationBuilder.RenameColumn(
                name: "CreatedBy",
                schema: "security",
                table: "user_temp_suspensions",
                newName: "created_by");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                schema: "security",
                table: "user_temp_suspensions",
                newName: "created_at");

            migrationBuilder.RenameIndex(
                name: "IX_user_temp_suspensions_UserId",
                schema: "security",
                table: "user_temp_suspensions",
                newName: "ix_user_temp_suspensions_user_id");

            migrationBuilder.RenameColumn(
                name: "Id",
                schema: "security",
                table: "user_credentials",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                schema: "security",
                table: "user_credentials",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                schema: "security",
                table: "user_credentials",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "RowVersion",
                schema: "security",
                table: "user_credentials",
                newName: "row_version");

            migrationBuilder.RenameColumn(
                name: "PasswordHash",
                schema: "security",
                table: "user_credentials",
                newName: "password_hash");

            migrationBuilder.RenameColumn(
                name: "PasswordChangedAt",
                schema: "security",
                table: "user_credentials",
                newName: "password_changed_at");

            migrationBuilder.RenameColumn(
                name: "MustChangePassword",
                schema: "security",
                table: "user_credentials",
                newName: "must_change_password");

            migrationBuilder.RenameColumn(
                name: "FailedLoginAttempts",
                schema: "security",
                table: "user_credentials",
                newName: "failed_login_attempts");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                schema: "security",
                table: "user_credentials",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "Code",
                schema: "identity",
                table: "tenants",
                newName: "code");

            migrationBuilder.RenameColumn(
                name: "Id",
                schema: "identity",
                table: "tenants",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedBy",
                schema: "identity",
                table: "tenants",
                newName: "updated_by");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                schema: "identity",
                table: "tenants",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "TenantType",
                schema: "identity",
                table: "tenants",
                newName: "tenant_type");

            migrationBuilder.RenameColumn(
                name: "TaxId",
                schema: "identity",
                table: "tenants",
                newName: "tax_id");

            migrationBuilder.RenameColumn(
                name: "RowVersion",
                schema: "identity",
                table: "tenants",
                newName: "row_version");

            migrationBuilder.RenameColumn(
                name: "LegalName",
                schema: "identity",
                table: "tenants",
                newName: "legal_name");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                schema: "identity",
                table: "tenants",
                newName: "is_active");

            migrationBuilder.RenameColumn(
                name: "CreatedBy",
                schema: "identity",
                table: "tenants",
                newName: "created_by");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                schema: "identity",
                table: "tenants",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "Purpose",
                schema: "security",
                table: "password_reset_tokens",
                newName: "purpose");

            migrationBuilder.RenameColumn(
                name: "Id",
                schema: "security",
                table: "password_reset_tokens",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                schema: "security",
                table: "password_reset_tokens",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "UsedAt",
                schema: "security",
                table: "password_reset_tokens",
                newName: "used_at");

            migrationBuilder.RenameColumn(
                name: "TokenHash",
                schema: "security",
                table: "password_reset_tokens",
                newName: "token_hash");

            migrationBuilder.RenameColumn(
                name: "RequestedBy",
                schema: "security",
                table: "password_reset_tokens",
                newName: "requested_by");

            migrationBuilder.RenameColumn(
                name: "ExpiresAt",
                schema: "security",
                table: "password_reset_tokens",
                newName: "expires_at");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                schema: "security",
                table: "password_reset_tokens",
                newName: "created_at");

            migrationBuilder.AddPrimaryKey(
                name: "pk_users",
                schema: "identity",
                table: "users",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_temp_suspensions",
                schema: "security",
                table: "user_temp_suspensions",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_credentials",
                schema: "security",
                table: "user_credentials",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_tenants",
                schema: "identity",
                table: "tenants",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_password_reset_tokens",
                schema: "security",
                table: "password_reset_tokens",
                column: "id");

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

            migrationBuilder.DropPrimaryKey(
                name: "pk_users",
                schema: "identity",
                table: "users");

            migrationBuilder.DropPrimaryKey(
                name: "pk_user_temp_suspensions",
                schema: "security",
                table: "user_temp_suspensions");

            migrationBuilder.DropPrimaryKey(
                name: "pk_user_credentials",
                schema: "security",
                table: "user_credentials");

            migrationBuilder.DropPrimaryKey(
                name: "pk_tenants",
                schema: "identity",
                table: "tenants");

            migrationBuilder.DropPrimaryKey(
                name: "pk_password_reset_tokens",
                schema: "security",
                table: "password_reset_tokens");

            migrationBuilder.RenameColumn(
                name: "status",
                schema: "identity",
                table: "users",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "email",
                schema: "identity",
                table: "users",
                newName: "Email");

            migrationBuilder.RenameColumn(
                name: "id",
                schema: "identity",
                table: "users",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_by",
                schema: "identity",
                table: "users",
                newName: "UpdatedBy");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                schema: "identity",
                table: "users",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "row_version",
                schema: "identity",
                table: "users",
                newName: "RowVersion");

            migrationBuilder.RenameColumn(
                name: "display_name",
                schema: "identity",
                table: "users",
                newName: "DisplayName");

            migrationBuilder.RenameColumn(
                name: "deleted_by",
                schema: "identity",
                table: "users",
                newName: "DeletedBy");

            migrationBuilder.RenameColumn(
                name: "deleted_at",
                schema: "identity",
                table: "users",
                newName: "DeletedAt");

            migrationBuilder.RenameColumn(
                name: "created_by",
                schema: "identity",
                table: "users",
                newName: "CreatedBy");

            migrationBuilder.RenameColumn(
                name: "created_at",
                schema: "identity",
                table: "users",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "reason",
                schema: "security",
                table: "user_temp_suspensions",
                newName: "Reason");

            migrationBuilder.RenameColumn(
                name: "id",
                schema: "security",
                table: "user_temp_suspensions",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_id",
                schema: "security",
                table: "user_temp_suspensions",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "updated_by",
                schema: "security",
                table: "user_temp_suspensions",
                newName: "UpdatedBy");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                schema: "security",
                table: "user_temp_suspensions",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                schema: "security",
                table: "user_temp_suspensions",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "starts_at",
                schema: "security",
                table: "user_temp_suspensions",
                newName: "StartsAt");

            migrationBuilder.RenameColumn(
                name: "row_version",
                schema: "security",
                table: "user_temp_suspensions",
                newName: "RowVersion");

            migrationBuilder.RenameColumn(
                name: "ends_at",
                schema: "security",
                table: "user_temp_suspensions",
                newName: "EndsAt");

            migrationBuilder.RenameColumn(
                name: "deleted_by",
                schema: "security",
                table: "user_temp_suspensions",
                newName: "DeletedBy");

            migrationBuilder.RenameColumn(
                name: "deleted_at",
                schema: "security",
                table: "user_temp_suspensions",
                newName: "DeletedAt");

            migrationBuilder.RenameColumn(
                name: "created_by",
                schema: "security",
                table: "user_temp_suspensions",
                newName: "CreatedBy");

            migrationBuilder.RenameColumn(
                name: "created_at",
                schema: "security",
                table: "user_temp_suspensions",
                newName: "CreatedAt");

            migrationBuilder.RenameIndex(
                name: "ix_user_temp_suspensions_user_id",
                schema: "security",
                table: "user_temp_suspensions",
                newName: "IX_user_temp_suspensions_UserId");

            migrationBuilder.RenameColumn(
                name: "id",
                schema: "security",
                table: "user_credentials",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_id",
                schema: "security",
                table: "user_credentials",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                schema: "security",
                table: "user_credentials",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "row_version",
                schema: "security",
                table: "user_credentials",
                newName: "RowVersion");

            migrationBuilder.RenameColumn(
                name: "password_hash",
                schema: "security",
                table: "user_credentials",
                newName: "PasswordHash");

            migrationBuilder.RenameColumn(
                name: "password_changed_at",
                schema: "security",
                table: "user_credentials",
                newName: "PasswordChangedAt");

            migrationBuilder.RenameColumn(
                name: "must_change_password",
                schema: "security",
                table: "user_credentials",
                newName: "MustChangePassword");

            migrationBuilder.RenameColumn(
                name: "failed_login_attempts",
                schema: "security",
                table: "user_credentials",
                newName: "FailedLoginAttempts");

            migrationBuilder.RenameColumn(
                name: "created_at",
                schema: "security",
                table: "user_credentials",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "code",
                schema: "identity",
                table: "tenants",
                newName: "Code");

            migrationBuilder.RenameColumn(
                name: "id",
                schema: "identity",
                table: "tenants",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_by",
                schema: "identity",
                table: "tenants",
                newName: "UpdatedBy");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                schema: "identity",
                table: "tenants",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "tenant_type",
                schema: "identity",
                table: "tenants",
                newName: "TenantType");

            migrationBuilder.RenameColumn(
                name: "tax_id",
                schema: "identity",
                table: "tenants",
                newName: "TaxId");

            migrationBuilder.RenameColumn(
                name: "row_version",
                schema: "identity",
                table: "tenants",
                newName: "RowVersion");

            migrationBuilder.RenameColumn(
                name: "legal_name",
                schema: "identity",
                table: "tenants",
                newName: "LegalName");

            migrationBuilder.RenameColumn(
                name: "is_active",
                schema: "identity",
                table: "tenants",
                newName: "IsActive");

            migrationBuilder.RenameColumn(
                name: "created_by",
                schema: "identity",
                table: "tenants",
                newName: "CreatedBy");

            migrationBuilder.RenameColumn(
                name: "created_at",
                schema: "identity",
                table: "tenants",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "purpose",
                schema: "security",
                table: "password_reset_tokens",
                newName: "Purpose");

            migrationBuilder.RenameColumn(
                name: "id",
                schema: "security",
                table: "password_reset_tokens",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_id",
                schema: "security",
                table: "password_reset_tokens",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "used_at",
                schema: "security",
                table: "password_reset_tokens",
                newName: "UsedAt");

            migrationBuilder.RenameColumn(
                name: "token_hash",
                schema: "security",
                table: "password_reset_tokens",
                newName: "TokenHash");

            migrationBuilder.RenameColumn(
                name: "requested_by",
                schema: "security",
                table: "password_reset_tokens",
                newName: "RequestedBy");

            migrationBuilder.RenameColumn(
                name: "expires_at",
                schema: "security",
                table: "password_reset_tokens",
                newName: "ExpiresAt");

            migrationBuilder.RenameColumn(
                name: "created_at",
                schema: "security",
                table: "password_reset_tokens",
                newName: "CreatedAt");

            migrationBuilder.AddPrimaryKey(
                name: "PK_users",
                schema: "identity",
                table: "users",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_user_temp_suspensions",
                schema: "security",
                table: "user_temp_suspensions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_user_credentials",
                schema: "security",
                table: "user_credentials",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_tenants",
                schema: "identity",
                table: "tenants",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_password_reset_tokens",
                schema: "security",
                table: "password_reset_tokens",
                column: "Id");
        }
    }
}
