using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// ICT — correlación idempotente del borrador con el pre-trámite. Añade columnas aditivas
    /// <c>origin</c> / <c>external_ref</c> a <c>tramites.procedure_instances</c> + índice único parcial
    /// <c>uq_procedure_instances_tenant_external_ref</c>, vía DDL embebido idempotente
    /// (<c>40-ICT-procedure-external-ref.sql</c>). La tabla está ExcludeFromMigrations, por eso el diff
    /// de EF sale vacío y el cuerpo se escribe a mano (mismo patrón que F_ICT_ProcedurePause). Down reversible.
    /// </remarks>
    public partial class F_ICT_ProcedureExternalRef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("40-ICT-procedure-external-ref.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS tramites.uq_procedure_instances_tenant_external_ref;
                ALTER TABLE tramites.procedure_instances
                    DROP COLUMN IF EXISTS origin,
                    DROP COLUMN IF EXISTS external_ref;
                """);
    }
}
