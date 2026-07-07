using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ADR0023_MandateSigners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mandate_signers",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    transit_office_id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    document_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    integrity_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mandate_signers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mandate_signer_companies",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    mandate_signer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transit_office_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mandate_signer_companies", x => x.id);
                    table.ForeignKey(
                        name: "fk_mandate_signer_companies_mandate_signers_mandate_signer_id",
                        column: x => x.mandate_signer_id,
                        principalSchema: "admin",
                        principalTable: "mandate_signers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mandate_signer_companies_mandate_signer_id",
                schema: "admin",
                table: "mandate_signer_companies",
                column: "mandate_signer_id");

            migrationBuilder.CreateIndex(
                name: "uq_mandate_signer_companies_active",
                schema: "admin",
                table: "mandate_signer_companies",
                columns: new[] { "transit_office_id", "company_tenant_id" },
                unique: true,
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_mandate_signers_transit_office_id_is_active",
                schema: "admin",
                table: "mandate_signers",
                columns: new[] { "transit_office_id", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mandate_signer_companies",
                schema: "admin");

            migrationBuilder.DropTable(
                name: "mandate_signers",
                schema: "admin");
        }
    }
}
