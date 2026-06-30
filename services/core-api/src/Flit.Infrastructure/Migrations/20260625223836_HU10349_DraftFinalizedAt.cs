using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10349 — Desacoplar la validación de identidad (fase 2). Agrega
    /// <c>tramites.procedure_instances.draft_finalized_at</c> (marca de "borrador finalizado":
    /// datos completos a la espera de la validación de identidad async del cliente) + índice parcial
    /// para la cola de borradores finalizados que consume el auto-flujo. La tabla está
    /// <c>ExcludeFromMigrations</c> (DDL gestionado por SQL crudo, HU #10150), por eso el diff EF queda
    /// vacío y la columna se agrega con SQL idempotente.
    /// </remarks>
    public partial class HU10349_DraftFinalizedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE tramites.procedure_instances
                  ADD COLUMN IF NOT EXISTS draft_finalized_at timestamptz NULL;

                COMMENT ON COLUMN tramites.procedure_instances.draft_finalized_at IS
                  'HU #10349 — borrador finalizado: datos completos a la espera de validación de identidad async. NULL = no finalizado. No equivale a radicar.';

                -- Cola de borradores finalizados por tenant (consumidor de IdentityValidationCompleted).
                CREATE INDEX IF NOT EXISTS ix_procedure_instances_draft_finalized
                  ON tramites.procedure_instances(tenant_id, draft_finalized_at)
                  WHERE status = 'draft' AND draft_finalized_at IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS tramites.ix_procedure_instances_draft_finalized;
                ALTER TABLE tramites.procedure_instances
                  DROP COLUMN IF EXISTS draft_finalized_at;
                """);
        }
    }
}
