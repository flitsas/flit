using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #12193 (Feature #12189) — índices funcionales que hacen sargable la búsqueda por placa.
    /// DDL en <c>Persistence/Sql/Ddl/105-HU12193-indices-funcionales-placa.sql</c>.
    ///
    /// Solo crea índices: no toca columnas, constraints ni datos, así que el <c>Down</c> es un
    /// DROP simétrico y la migración es reversible sin pérdida.
    ///
    /// Sin <c>CREATE INDEX CONCURRENTLY</c>: esta migración corre dentro de la transacción de EF y
    /// CONCURRENTLY no lo admite (ningún script de la carpeta Ddl lo usa). Ver el encabezado del .sql.
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260908140000_HU12193_IndicesFuncionalesPlaca")]
    public partial class HU12193_IndicesFuncionalesPlaca : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("105-HU12193-indices-funcionales-placa.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Los índices de la DDL 47 sobre las columnas crudas (tenant_id, plate) y (tenant_id, vin)
            // siguen existiendo: revertir esto devuelve el rendimiento anterior, no rompe ninguna consulta.
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS tramites.ix_procedure_instances_tenant_id_vin_upper;
                DROP INDEX IF EXISTS tramites.ix_procedure_instances_plate_upper;
                DROP INDEX IF EXISTS tramites.ix_procedure_instances_tenant_id_plate_upper;
                """);
        }
    }
}
