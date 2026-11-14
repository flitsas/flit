using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class R9_AjusteConTramite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ajuste_con_tramite",
                schema: "diagnostico",
                table: "dictamenes",
                type: "boolean",
                nullable: false,
                defaultValueSql: "false",
                comment: "True si el vehículo ya tenía trámite cuando FLIT confirmó el ajuste (confirmar_con_tramite). Solo en ajustes; solo backoffice. [ADR-0084]");

            migrationBuilder.AddCheckConstraint(
                name: "ck_dictamenes_ajuste_con_tramite",
                schema: "diagnostico",
                table: "dictamenes",
                sql: "NOT ajuste_con_tramite OR analisis_override");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Guarda del rollback (patrón de R7): bajar la columna borraría la auditoría de los ajustes confirmados con trámite.
            migrationBuilder.Sql(@"DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM diagnostico.dictamenes WHERE ajuste_con_tramite) THEN
    RAISE EXCEPTION 'Rollback de R9_AjusteConTramite bloqueado: hay ajustes FLIT confirmados con trámite (ajuste_con_tramite). Exportar y decidir con el LT antes del rollback.';
  END IF;
END $$;");

            migrationBuilder.DropCheckConstraint(
                name: "ck_dictamenes_ajuste_con_tramite",
                schema: "diagnostico",
                table: "dictamenes");

            migrationBuilder.DropColumn(
                name: "ajuste_con_tramite",
                schema: "diagnostico",
                table: "dictamenes");
        }
    }
}
