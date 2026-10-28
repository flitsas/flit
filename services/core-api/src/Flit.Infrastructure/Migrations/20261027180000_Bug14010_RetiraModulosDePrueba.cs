using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Bug #14010: retira del catálogo RBAC los módulos que dejaron las pruebas de HU #10664
/// («Módulo de prueba HU10664-*»). No son módulos de producto.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261027180000_Bug14010_RetiraModulosDePrueba")]
public partial class Bug14010_RetiraModulosDePrueba : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            UPDATE security.permissions p
               SET is_active = false,
                   deleted_at = now()
              FROM security.modules m
             WHERE p.module_id = m.id
               AND p.deleted_at IS NULL
               AND (m.code LIKE 'HU10664-%' OR m.name LIKE 'Módulo de prueba HU10664%');

            UPDATE security.modules
               SET is_active = false,
                   deleted_at = now()
             WHERE deleted_at IS NULL
               AND (code LIKE 'HU10664-%' OR name LIKE 'Módulo de prueba HU10664%');
            """);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Los módulos de prueba no se restauran: no pertenecen al catálogo.
    }
}
