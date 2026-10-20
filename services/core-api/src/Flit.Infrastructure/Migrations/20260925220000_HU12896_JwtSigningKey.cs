using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #12896 (Feature #12886, FLIT Suite, tarea A-03) — <c>security.jwt_signing_keys</c>: llave de firma del JWT
/// persistente y cifrada. Sin entidad EF: la lee <c>PersistentJwtSigningKeyStore</c> con SQL directo. DDL:
/// <c>123-HU12896-jwt-signing-key.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260925220000_HU12896_JwtSigningKey")]
public partial class HU12896_JwtSigningKey : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("123-HU12896-jwt-signing-key.sql"));

    /// <inheritdoc />
    /// <remarks>Al revertir, la API vuelve a la llave efímera: los tokens emitidos con la persistente dejan de validar.</remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP TABLE IF EXISTS security.jwt_signing_keys;");
}
