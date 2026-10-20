using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Epic #13217 (FLIT Suite) — se retira el producto <c>demo</c> del catálogo: los primeros productos nuevos son
/// Comparendos y Diagnóstico. Solo datos, sin cambio de esquema. DDL: <c>125-suite-retirar-producto-demo.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261002120000_Suite_RetirarProductoDemo")]
public partial class Suite_RetirarProductoDemo : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("125-suite-retirar-producto-demo.sql"));

    /// <inheritdoc />
    /// <remarks>Vuelve a dejar el producto en el catálogo; su habilitación por empresa y su cliente OIDC no se recuperan.</remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            INSERT INTO platform.products (code, name, icon, status, sort_order)
            VALUES ('demo', 'Demo', 'flask-conical', 'active', 90)
            ON CONFLICT (code) DO UPDATE SET status = 'active';
            """);
}
