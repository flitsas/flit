using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HU10664_DropTenantModuleGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_module_grants",
                schema: "security");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_module_grants",
                schema: "security",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    module_id = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    granted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_module_grants", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenant_module_grants_security_modules_module_id",
                        column: x => x.module_id,
                        principalSchema: "security",
                        principalTable: "modules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_module_grants_module_id",
                schema: "security",
                table: "tenant_module_grants",
                column: "module_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_module_grants_tenant_id_module_id",
                schema: "security",
                table: "tenant_module_grants",
                columns: new[] { "tenant_id", "module_id" },
                unique: true);
        }
    }
}
