using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #12990 (Feature #12886, FLIT Suite, tarea A-05) — tablas <c>identity.oidc_*</c> del servidor OIDC del hub
/// (OpenIddict). DDL: <c>124-HU12990-oidc-stores.sql</c>. Las entidades de OpenIddict se mapean en
/// <c>FlitDbContext</c> con <c>ExcludeFromMigrations</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260928120000_HU12990_OidcStores")]
public partial class HU12990_OidcStores : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("124-HU12990-oidc-stores.sql"));

    /// <inheritdoc />
    /// <remarks>Al revertir se pierden los clientes, las autorizaciones y los refresh tokens: todos vuelven a iniciar sesión.</remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS identity.oidc_tokens;
            DROP TABLE IF EXISTS identity.oidc_authorizations;
            DROP TABLE IF EXISTS identity.oidc_scopes;
            DROP TABLE IF EXISTS identity.oidc_applications;
            """);
}
