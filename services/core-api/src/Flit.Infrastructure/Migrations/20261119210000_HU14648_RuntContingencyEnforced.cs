using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Épica #14644 (HU #14648) — marca como exigido (<c>enforced = 'true'</c>) el permiso <c>runt_contingency.manage</c> de la
/// contingencia de proveedor RUNT. DDL: <c>204-HU14648-runt-contingency-enforced.sql</c>. Lleva los atributos inline (sin
/// Designer): sin ellos la migración no se descubre y no corre al arrancar.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261119210000_HU14648_RuntContingencyEnforced")]
public partial class HU14648_RuntContingencyEnforced : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("204-HU14648-runt-contingency-enforced.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("UPDATE security.permissions SET enforced = 'false' WHERE slug = 'runt_contingency.manage';");
}
