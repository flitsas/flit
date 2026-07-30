using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// FEATURE-08 / HU-BE-07 (CFD-10) — Seeds de tipos de referencia como configuraciones:
    /// <c>MATRICULA_NUEVA</c>, <c>TRASPASO_STANDARD</c> (solo gate_profile), 
    /// <c>PRENDA_INSCRIPCION</c> y <c>CAMBIO_LOCATARIO</c> (config completa). Idempotente. DDL:
    /// 38-F08-seeds-tipos-configurados.sql. El flag F08_DynamicProcedures se activa por tenant en DEV
    /// (fuera de esta migración). ⚠️ Designer.cs omitido — regenerar con <c>dotnet ef migrations add</c>.
    /// </remarks>
    // Atributos inline (patrón HU10774): sin ellos EF no descubre la migración y el seed de los 4
    // tipos configurados nunca corre en DEV. Ver F08_ConformationProfile para el detalle.
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260721100300_F08_SeedTiposConfigurados")]
    public partial class F08_SeedTiposConfigurados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("38-F08-seeds-tipos-configurados.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("""
                -- No borra MATRICULA_NUEVA / TRASPASO_STANDARD (tipos operativos).
                DELETE FROM tramites.procedure_types
                WHERE code IN ('PRENDA_INSCRIPCION', 'CAMBIO_LOCATARIO');
                """);
    }
}
