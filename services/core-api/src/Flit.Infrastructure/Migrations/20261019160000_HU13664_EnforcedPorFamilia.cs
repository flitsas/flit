using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #13664 (Feature #13636 F-F, Épica #12753) — los 6 permisos Ver/Crear por familia de trámite pasan a enforced = 'true'.
/// DDL: <c>146-HU13664-enforced-por-familia.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261019160000_HU13664_EnforcedPorFamilia")]
public partial class HU13664_EnforcedPorFamilia : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("146-HU13664-enforced-por-familia.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            UPDATE security.permissions SET enforced = 'false'
             WHERE slug IN ('matriculas.read', 'matriculas.create', 'traspasos.read', 'traspasos.create',
                            'otros-tramites.read', 'otros-tramites.create');
            """);
}
