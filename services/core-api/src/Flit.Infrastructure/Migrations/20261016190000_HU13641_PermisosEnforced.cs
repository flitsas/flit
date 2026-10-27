using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #13641 (Feature #13100 FA-6, Épica #12753) — columna <c>security.permissions.enforced</c> (true/false/excluded).
/// DDL: <c>139-HU13641-permisos-enforced.sql</c>. Sin mapear en la entidad: lo leen el test de arquitectura y SQL.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261016190000_HU13641_PermisosEnforced")]
public partial class HU13641_PermisosEnforced : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("139-HU13641-permisos-enforced.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            ALTER TABLE security.permissions DROP CONSTRAINT IF EXISTS ck_permissions_enforced;
            ALTER TABLE security.permissions DROP COLUMN IF EXISTS enforced;
            """);
}
