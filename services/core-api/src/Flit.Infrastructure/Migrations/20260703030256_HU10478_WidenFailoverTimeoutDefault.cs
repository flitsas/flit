using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HU10478_WidenFailoverTimeoutDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "runt_failover_timeout_ms",
                schema: "admin",
                table: "tenant_operational_policies",
                type: "integer",
                nullable: false,
                defaultValue: 60000,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 4000);

            // Sube las filas que aún tenían el viejo default (4s, demasiado corto: el RUNT en frío
            // tarda más y caía al fallback prematuramente). No toca tenants que hayan fijado otro valor.
            migrationBuilder.Sql(
                "UPDATE admin.tenant_operational_policies SET runt_failover_timeout_ms = 60000 WHERE runt_failover_timeout_ms = 4000;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "runt_failover_timeout_ms",
                schema: "admin",
                table: "tenant_operational_policies",
                type: "integer",
                nullable: false,
                defaultValue: 4000,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 60000);
        }
    }
}
