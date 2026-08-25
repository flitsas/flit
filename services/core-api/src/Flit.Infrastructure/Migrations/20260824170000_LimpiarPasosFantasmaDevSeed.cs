using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// ADR-0050 — retira los pasos que los seeds de desarrollo dejaban en <c>sort_order = 1</c>,
/// duplicando la primera posición de los dos tipos canónicos.
/// DDL: <c>86-limpiar-pasos-fantasma-dev-seed.sql</c>.
/// <para>El origen ya está cortado en los propios seeds; esto limpia lo sembrado. En producción es
/// un no-op: el sembrador de desarrollo no corre allí.</para>
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260824170000_LimpiarPasosFantasmaDevSeed")]
public partial class LimpiarPasosFantasmaDevSeed : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("86-limpiar-pasos-fantasma-dev-seed.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// No se recrean: eran configuración de relleno de cuando los tipos no tenían recorrido, y
    /// volver a insertarlos reintroduciría el defecto. Revertir esta migración no devuelve un estado
    /// deseable, así que deliberadamente no hace nada.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intencionalmente vacío. Ver el remark.
    }
}
