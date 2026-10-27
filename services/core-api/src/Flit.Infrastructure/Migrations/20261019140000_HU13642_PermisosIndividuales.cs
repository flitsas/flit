using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #13642 (Feature #13101 F-B, Épica #12753, ADR-0073) — <c>security.user_permission_overrides</c> y la bitácora
/// append-only <c>security.user_permission_audit</c>. DDL: <c>144-HU13642-permisos-individuales.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261019140000_HU13642_PermisosIndividuales")]
public partial class HU13642_PermisosIndividuales : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("144-HU13642-permisos-individuales.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS security.user_permission_audit;
            DROP FUNCTION IF EXISTS security.trg_user_permission_audit_immutable();
            DROP TABLE IF EXISTS security.user_permission_overrides;
            """);
}
