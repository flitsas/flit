using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <summary>
    /// HU #14563 (B1, ADR-0086 D-1): versión de fila del tablero en <c>diagnostico.vehiculos</c> (<c>version_tablero</c>,
    /// <c>ck_vehiculos_version_tablero</c>, <c>ix_vehiculos_tablero</c>). Sin backfill: 0 = sin cambios de fila.
    /// </summary>
    public partial class R1_VersionFilaTablero : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "version_tablero",
                schema: "diagnostico",
                table: "vehiculos",
                type: "bigint",
                nullable: false,
                defaultValueSql: "0",
                comment: "Valor de diagnosticos.version_tablero en el último cambio de la FILA (dictamen nuevo o ajustado, trámite creado/omitido/fallido). 0 = sin cambios de fila. Polling ?desde=. [ADR-0086]");

            migrationBuilder.CreateIndex(
                name: "ix_vehiculos_tablero",
                schema: "diagnostico",
                table: "vehiculos",
                columns: new[] { "tenant_id", "diagnostico_id", "version_tablero" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_vehiculos_version_tablero",
                schema: "diagnostico",
                table: "vehiculos",
                sql: "version_tablero >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_vehiculos_tablero",
                schema: "diagnostico",
                table: "vehiculos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vehiculos_version_tablero",
                schema: "diagnostico",
                table: "vehiculos");

            migrationBuilder.DropColumn(
                name: "version_tablero",
                schema: "diagnostico",
                table: "vehiculos");
        }
    }
}
