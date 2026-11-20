using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class D2_ReprocesoUsuarioCelda : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "reproceso_usuario_id",
                schema: "diagnostico",
                table: "consultas",
                type: "uuid",
                nullable: true,
                comment: "@pii:low Usuario del reproceso pendiente (claim sub, HU #13746, R-1): la siguiente ejecución deja su intento con disparador REPROCESO y este usuario; null si no hay reproceso pendiente.");

            migrationBuilder.AddCheckConstraint(
                name: "ck_consultas_reproceso_usuario",
                schema: "diagnostico",
                table: "consultas",
                sql: "reproceso_usuario_id IS NULL OR estado IN ('PENDIENTE', 'EN_CURSO')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_consultas_reproceso_usuario",
                schema: "diagnostico",
                table: "consultas");

            migrationBuilder.DropColumn(
                name: "reproceso_usuario_id",
                schema: "diagnostico",
                table: "consultas");
        }
    }
}
