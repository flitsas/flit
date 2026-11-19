using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Mejora sin HU — alcance de compañías cliente por usuario de un organismo de tránsito
/// (<c>admin.ot_user_client_scopes</c> y <c>admin.ot_user_client_scope_clients</c>; ausencia de fila = todos).
/// DDL: <c>197-ot-usuario-alcance-clientes.sql</c>. Sin Designer: sin atributos no se descubre.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261117120000_OtUsuarioAlcanceClientes")]
public partial class OtUsuarioAlcanceClientes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("197-ot-usuario-alcance-clientes.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS admin.ot_user_client_scope_clients;
            DROP TABLE IF EXISTS admin.ot_user_client_scopes;
            """);
}
