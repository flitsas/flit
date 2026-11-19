using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Mejora sin HU — alcance de compañías cliente guardado en la invitación a un usuario de OT
/// (<c>admin.ot_invitation_client_scope_clients</c>). DDL: <c>198-ot-invitacion-alcance-clientes.sql</c>.
/// Sin Designer: sin atributos no se descubre.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261117130000_OtInvitacionAlcanceClientes")]
public partial class OtInvitacionAlcanceClientes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("198-ot-invitacion-alcance-clientes.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP TABLE IF EXISTS admin.ot_invitation_client_scope_clients;");
}
