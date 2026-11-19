using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14454 (Feature #14453, Épica #14210, ADR-0082) — producto OIDC <c>caja</c>, módulo y permiso <c>caja.cobrar</c> y rol
/// <c>ot_caja</c> atado a un organismo. DDL: <c>199-HU14454-producto-caja-rol-ot-caja.sql</c>. Sin Designer: sin los atributos
/// <c>[DbContext]</c>/<c>[Migration]</c> no se descubre y no corre al arrancar.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261117140000_HU14454_RolOtCaja")]
public partial class HU14454_RolOtCaja : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("199-HU14454-producto-caja-rol-ot-caja.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        // Orden inverso: asignaciones y grants del rol, rol, permiso, módulo y producto. Solo si nada más los usa.
        migrationBuilder.Sql("""
            DELETE FROM security.user_role_assignments
             WHERE role_id IN (SELECT id FROM security.roles WHERE code = 'ot_caja' AND target_entity_type = 'TRANSIT_OFFICE');
            DELETE FROM security.role_permissions
             WHERE role_id IN (SELECT id FROM security.roles WHERE code = 'ot_caja' AND target_entity_type = 'TRANSIT_OFFICE');
            DELETE FROM security.roles WHERE code = 'ot_caja' AND target_entity_type = 'TRANSIT_OFFICE';
            DELETE FROM security.permissions WHERE slug = 'caja.cobrar';
            DELETE FROM security.modules WHERE code = 'caja';
            DELETE FROM platform.tenant_products WHERE product_code = 'caja';
            DELETE FROM platform.products WHERE code = 'caja';
            """);
}
