using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.PersonaNatural.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConfiguracionRetencion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "configuracion_retencion",
                schema: "personanatural",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    habilitado = table.Column<bool>(type: "boolean", nullable: false),
                    dias_retencion_borrador = table.Column<int>(type: "integer", nullable: false),
                    intervalo_minutos = table.Column<int>(type: "integer", nullable: false),
                    lote = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_configuracion_retencion", x => x.id);
                    table.CheckConstraint("ck_configuracion_retencion", "id = 1 AND dias_retencion_borrador >= 1 AND intervalo_minutos >= 1 AND lote >= 1");
                });

            migrationBuilder.InsertData(
                schema: "personanatural",
                table: "configuracion_retencion",
                columns: new[] { "id", "dias_retencion_borrador", "habilitado", "intervalo_minutos", "lote", "updated_at" },
                values: new object[] { (short)1, 30, true, 1440, 200, new DateTimeOffset(new DateTime(2026, 10, 27, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "configuracion_retencion",
                schema: "personanatural");
        }
    }
}
