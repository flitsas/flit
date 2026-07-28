using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10916 (ADR-0036 §D9) — mandatario del mandato resuelto al aprobar. El DDL embebido
    /// (42-HU10916-procedure-instance-mandate-signer.sql) agrega la columna mandate_signer_id a
    /// tramites.procedure_instances con FK a admin.mandate_signers (ON DELETE SET NULL) e índice parcial.
    /// La tabla está ExcludeFromMigrations (mismo patrón que plate_flow_status/consolidado_maestro_vigente):
    /// el DDL crudo lleva el esquema y el snapshot solo refleja el modelo EF.
    /// </remarks>
    public partial class HU10916_ProcedureInstanceMandateSigner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("42-HU10916-procedure-instance-mandate-signer.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS tramites.ix_procedure_instances_mandate_signer_id;");
            migrationBuilder.Sql(
                "ALTER TABLE tramites.procedure_instances " +
                "DROP CONSTRAINT IF EXISTS fk_procedure_instances_mandate_signer;");
            migrationBuilder.Sql(
                "ALTER TABLE tramites.procedure_instances DROP COLUMN IF EXISTS mandate_signer_id;");
        }
    }
}
