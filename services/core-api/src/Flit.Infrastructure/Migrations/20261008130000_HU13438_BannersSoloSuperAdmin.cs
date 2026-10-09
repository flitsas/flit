using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #13438 (Feature #13436, Épica #12750) — retira el permiso <c>banners.manage</c> de todo rol distinto de
/// SuperAdmin (AdminCompany y roles personalizados). DDL: <c>133-HU13438-banners-solo-super-admin.sql</c>.
/// Solo datos (sin cambio de modelo EF), por eso lleva los atributos inline y no un Designer.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261008130000_HU13438_BannersSoloSuperAdmin")]
public partial class HU13438_BannersSoloSuperAdmin : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("133-HU13438-banners-solo-super-admin.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Restaura solo el estado sembrado original (AdminCompany con <c>banners.manage</c>). Los grants a roles
    /// personalizados que existían antes no se pueden reconstruir: la migración no guardó cuáles eran.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            INSERT INTO security.role_permissions (id, role_id, permission_id, created_at)
            SELECT uuidv7(), r.id, p.id, now()
            FROM security.roles r
            CROSS JOIN security.permissions p
            WHERE r.code = 'AdminCompany'
              AND p.slug = 'banners.manage'
              AND NOT EXISTS (
                  SELECT 1 FROM security.role_permissions x
                  WHERE x.role_id = r.id AND x.permission_id = p.id);
            """);
}
