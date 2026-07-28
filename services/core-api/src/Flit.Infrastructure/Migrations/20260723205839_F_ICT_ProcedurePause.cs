using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// ICT — primitivo de PAUSA en <c>tramites.procedure_instances</c> (servicio v1 pauseDraftProcess
    /// + <c>starts_procedure_in_paused</c> del register). FLIT 2.0 no tenía pausa; se añaden columnas
    /// aditivas <c>is_paused</c> / <c>paused_observation</c> vía DDL embebido idempotente
    /// (<c>39-ICT-procedure-pause.sql</c>). Down reversible.
    /// (El diff de section_type detectado al generar era drift de la migración F08 sin Designer; el
    /// snapshot regenerado lo corrige, pero el Up de ESTA migración solo toca las columnas de pausa.)
    /// </remarks>
    public partial class F_ICT_ProcedurePause : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("39-ICT-procedure-pause.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("""
                ALTER TABLE tramites.procedure_instances
                    DROP COLUMN IF EXISTS is_paused,
                    DROP COLUMN IF EXISTS paused_observation;
                """);
    }
}
