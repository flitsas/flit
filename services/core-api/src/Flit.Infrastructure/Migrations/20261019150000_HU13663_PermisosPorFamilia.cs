using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #13663 (Feature #13636 F-F, Épica #12753) — catálogo de permisos Ver/Crear por familia de trámite (Matrículas, Traspasos,
/// Otros trámites) con backfill desde <c>tramites.read</c>/<c>tramites.create</c>. DDL: <c>145-HU13663-permisos-por-familia.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261019150000_HU13663_PermisosPorFamilia")]
public partial class HU13663_PermisosPorFamilia : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("145-HU13663-permisos-por-familia.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DELETE FROM security.role_permissions
             WHERE permission_id IN (SELECT id FROM security.permissions
                                      WHERE slug IN ('matriculas.read', 'matriculas.create', 'traspasos.read', 'traspasos.create',
                                                     'otros-tramites.read', 'otros-tramites.create'));
            DELETE FROM security.permissions
             WHERE slug IN ('matriculas.read', 'matriculas.create', 'traspasos.read', 'traspasos.create',
                            'otros-tramites.read', 'otros-tramites.create');
            DELETE FROM security.modules WHERE code IN ('tramites-matriculas', 'tramites-traspasos', 'tramites-otros');
            """);
}
