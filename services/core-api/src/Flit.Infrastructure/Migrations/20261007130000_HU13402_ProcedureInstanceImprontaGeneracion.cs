using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #13402 (Feature #13399, Épica #13039) — el trámite conserva el parámetro «generar improntas»
    /// vigente al crearse: columna <c>impronta_generacion_habilitada</c> en
    /// <c>tramites.procedure_instances</c> (boolean NOT NULL DEFAULT true; los trámites existentes
    /// quedan en true = comportamiento histórico). ALTER idempotente, mismo patrón que HU13400.
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20261007130000_HU13402_ProcedureInstanceImprontaGeneracion")]
    public partial class HU13402_ProcedureInstanceImprontaGeneracion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE tramites.procedure_instances
                    ADD COLUMN IF NOT EXISTS impronta_generacion_habilitada boolean NOT NULL DEFAULT true;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE tramites.procedure_instances
                    DROP COLUMN IF EXISTS impronta_generacion_habilitada;
                """);
        }
    }
}
