using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Migración V1→V2 (traspasos) — agrega <c>tramites.procedure_instances.is_migrated</c>
    /// (bool, default false): marca de "trámite histórico importado desde Flit V1". En true el
    /// trámite es una FOTO de solo lectura y, en estado terminal, el wizard lo reporta como
    /// expediente completo (no lo somete al gating vivo que exige datos que la migración no
    /// reconstruye —comercial, biométrica, FUR—). La setea el migrador Flit.DataMigration.V1.
    /// La tabla está <c>ExcludeFromMigrations</c> (DDL gestionado por SQL crudo, HU #10150), por eso
    /// el diff EF queda vacío y la columna se agrega con SQL idempotente.
    ///
    /// Migración hand-authored: atributos <c>[DbContext]</c> + <c>[Migration]</c> inline y sin
    /// Designer (patrón HU #10536 / N03). Sin estos atributos EF NO descubre la migración (queda
    /// fuera de <c>GetPendingMigrations</c>) y <c>Migrate()</c> nunca la aplica en los despliegues.
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260727120000_MigracionV1_IsMigrated")]
    public partial class MigracionV1_IsMigrated : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE tramites.procedure_instances
                  ADD COLUMN IF NOT EXISTS is_migrated boolean NOT NULL DEFAULT false;

                COMMENT ON COLUMN tramites.procedure_instances.is_migrated IS
                  'Migración V1->V2 — trámite histórico importado desde Flit V1 (foto de solo lectura). En estado terminal el wizard lo reporta como expediente completo, sin gating vivo. La setea el migrador Flit.DataMigration.V1.';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE tramites.procedure_instances
                  DROP COLUMN IF EXISTS is_migrated;
                """);
        }
    }
}
