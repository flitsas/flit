using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #12790 (Épica #12760) — sello de tiempo de generación de cada consolidado. Agrega
    /// <c>tramites.procedure_instances.consolidado_wizard_generado_en</c> y
    /// <c>consolidado_maestro_generado_en</c> (timestamptz, nulables, sin default): los sellan con el
    /// instante UTC <c>GenerarConsolidadoHandler</c> / <c>CargarConsolidadoExternoHandler</c> (wizard)
    /// y <c>GenerarConsolidadoMaestroHandler</c> (maestro), junto a la bandera de vigencia. Los
    /// trámites históricos quedan en null («fecha no disponible»); la migración NO rellena ni invalida
    /// nada. La tabla está <c>ExcludeFromMigrations</c> (DDL por SQL crudo, HU #10150), por eso el diff
    /// EF queda vacío y las columnas se agregan con SQL idempotente (<c>IF NOT EXISTS</c>).
    ///
    /// El snapshot regenerado incorpora además <c>ProcedureRevocationRequest</c> y
    /// <c>RevocationRequestEmailDispatch</c> (HU #12571/#12579), que faltaban en él: ambas son
    /// <c>ExcludeFromMigrations</c>, así que no generan DDL; solo se alinea snapshot con modelo.
    /// </remarks>
    public partial class HU12790_ConsolidadoGeneradoEn : Migration
    {
        /// <summary>SQL idempotente del Up (AC1): reaplicarlo no produce error.</summary>
        private const string UpSql =
            """
            ALTER TABLE tramites.procedure_instances
              ADD COLUMN IF NOT EXISTS consolidado_wizard_generado_en timestamptz NULL;

            ALTER TABLE tramites.procedure_instances
              ADD COLUMN IF NOT EXISTS consolidado_maestro_generado_en timestamptz NULL;

            COMMENT ON COLUMN tramites.procedure_instances.consolidado_wizard_generado_en IS
              'HU #12790 - instante UTC en que se genero (o el SuperAdmin cargo a mano) el consolidado del wizard vigente. NULL = tramite historico, fecha no disponible (no invalida el documento).';

            COMMENT ON COLUMN tramites.procedure_instances.consolidado_maestro_generado_en IS
              'HU #12790 - instante UTC en que se genero el consolidado maestro vigente. NULL = tramite historico, fecha no disponible (no invalida el documento).';
            """;

        /// <summary>SQL idempotente del Down.</summary>
        private const string DownSql =
            """
            ALTER TABLE tramites.procedure_instances
              DROP COLUMN IF EXISTS consolidado_maestro_generado_en;

            ALTER TABLE tramites.procedure_instances
              DROP COLUMN IF EXISTS consolidado_wizard_generado_en;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(UpSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DownSql);
        }
    }
}
