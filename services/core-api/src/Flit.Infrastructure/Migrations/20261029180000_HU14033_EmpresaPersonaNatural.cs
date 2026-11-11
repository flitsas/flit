using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14033 (Épica #14021) — empresa «Persona Natural» (<c>identity.tenants</c>, id fijo de
/// <c>PlatformTenants.PersonaNatural</c>). Idempotente: en DEV la fila ya existe y no se modifica.
/// DDL: <c>167-HU14033-empresa-persona-natural.sql</c>. Sin Designer: sin los atributos la migración no se descubre.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261029180000_HU14033_EmpresaPersonaNatural")]
public partial class HU14033_EmpresaPersonaNatural : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("167-HU14033-empresa-persona-natural.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DELETE FROM identity.tenants WHERE id = '00000000-0000-0000-0000-0000000f1171';
            """);
}
