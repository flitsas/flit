using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P7b_PlacaMotocarro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_vehiculos_tipo_identificador",
                schema: "diagnostico",
                table: "vehiculos");

            migrationBuilder.AlterColumn<string>(
                name: "tipo_identificador",
                schema: "diagnostico",
                table: "vehiculos",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                comment: "PLACA_CARRO, PLACA_MOTO, PLACA_REMOLQUE, PLACA_MAQUINARIA, PLACA_MOTOCARRO o VIN (R-19).",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldComment: "PLACA_CARRO, PLACA_MOTO, PLACA_REMOLQUE, PLACA_MAQUINARIA o VIN (R-19).");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vehiculos_tipo_identificador",
                schema: "diagnostico",
                table: "vehiculos",
                sql: "tipo_identificador IN ('PLACA_CARRO', 'PLACA_MOTO', 'PLACA_REMOLQUE', 'PLACA_MAQUINARIA', 'PLACA_MOTOCARRO', 'VIN')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revisión 2026-11-11 (H4): con vehículos PLACA_MOTOCARRO el CHECK anterior no se puede reponer. Se detiene antes de
            // tocar nada, con un mensaje que dice qué hacer, en vez de la violación genérica del CHECK.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM diagnostico.vehiculos WHERE tipo_identificador = 'PLACA_MOTOCARRO') THEN
                        RAISE EXCEPTION 'Rollback de P7b_PlacaMotocarro bloqueado: hay vehículos con tipo_identificador PLACA_MOTOCARRO; hay que reclasificar o eliminar esos vehículos (diagnostico.vehiculos) antes del rollback.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_vehiculos_tipo_identificador",
                schema: "diagnostico",
                table: "vehiculos");

            migrationBuilder.AlterColumn<string>(
                name: "tipo_identificador",
                schema: "diagnostico",
                table: "vehiculos",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                comment: "PLACA_CARRO, PLACA_MOTO, PLACA_REMOLQUE, PLACA_MAQUINARIA o VIN (R-19).",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldComment: "PLACA_CARRO, PLACA_MOTO, PLACA_REMOLQUE, PLACA_MAQUINARIA, PLACA_MOTOCARRO o VIN (R-19).");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vehiculos_tipo_identificador",
                schema: "diagnostico",
                table: "vehiculos",
                sql: "tipo_identificador IN ('PLACA_CARRO', 'PLACA_MOTO', 'PLACA_REMOLQUE', 'PLACA_MAQUINARIA', 'VIN')");
        }
    }
}
