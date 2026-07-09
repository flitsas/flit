using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IdentityValidationAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "identity_validation_audit",
                schema: "tramites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    stage = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    outcome = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    procedure_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    validation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kyverum_verification_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    party_role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    http_status = table.Column<int>(type: "integer", nullable: true),
                    signature_present = table.Column<bool>(type: "boolean", nullable: true),
                    secret_present = table.Column<bool>(type: "boolean", nullable: true),
                    decrypt_ok = table.Column<bool>(type: "boolean", nullable: true),
                    provider_status = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    error_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    detail = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identity_validation_audit", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_identity_validation_audit_kyverum",
                schema: "tramites",
                table: "identity_validation_audit",
                column: "kyverum_verification_id",
                filter: "kyverum_verification_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_identity_validation_audit_tenant",
                schema: "tramites",
                table: "identity_validation_audit",
                columns: new[] { "tenant_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_identity_validation_audit_validation",
                schema: "tramites",
                table: "identity_validation_audit",
                columns: new[] { "validation_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identity_validation_audit",
                schema: "tramites");
        }
    }
}
