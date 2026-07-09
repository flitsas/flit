using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10536 — Marcado de trámites prioritarios. Agrega
    /// <c>tramites.procedure_instances.prioritario</c> (bool, default false): el gestor lo marca para
    /// que el OT revise el trámite con primacía. Solo afecta el ordenamiento de los listados (operación
    /// y bandeja del OT); no altera el ciclo de vida. La tabla está <c>ExcludeFromMigrations</c> (DDL
    /// gestionado por SQL crudo, HU #10150), por eso el diff EF queda vacío y la columna + índice se
    /// agregan con SQL idempotente.
    /// Nota (Reportes 2.0): esta clase carecía de los atributos [DbContext]/[Migration] (no tiene
    /// Designer), por lo que EF NUNCA la descubría ni aplicaba — en una BD fresca la columna no
    /// existía y los endpoints de instancias fallaban con 42703. Los atributos la hacen visible;
    /// el SQL ya era idempotente, así que es segura en BDs donde la columna se creó por otra vía.
    ///
    /// Migración hand-authored: atributos <c>[DbContext]</c> + <c>[Migration]</c> inline y sin Designer
    /// (patrón N03 / HU #10198). Sin estos atributos EF NO descubre la migración (queda fuera de
    /// <c>GetPendingMigrations</c>) y <c>Migrate()</c> nunca la aplica en los despliegues.
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260706120000_HU10536_TramitePrioritario")]
    public partial class HU10536_TramitePrioritario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE tramites.procedure_instances
                  ADD COLUMN IF NOT EXISTS prioritario boolean NOT NULL DEFAULT false;

                COMMENT ON COLUMN tramites.procedure_instances.prioritario IS
                  'HU #10536 — trámite marcado como prioritario por el gestor: el OT lo revisa con primacía. Solo afecta el ordenamiento de los listados; no altera el ciclo de vida.';

                -- Sostiene el ordenamiento con primacía (prioritarios primero, luego por fecha) por tenant.
                CREATE INDEX IF NOT EXISTS ix_procedure_instances_prioritario
                  ON tramites.procedure_instances(tenant_id, prioritario, created_at);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS tramites.ix_procedure_instances_prioritario;
                ALTER TABLE tramites.procedure_instances
                  DROP COLUMN IF EXISTS prioritario;
                """);
        }
    }
}
