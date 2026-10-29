using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14071 (Feature J, Épica #14021) — módulo RBAC <c>persona-natural-admin</c> y permiso
/// <c>platform.persona_natural.manage</c> (solo SuperAdmin) de la administración del portal de Persona Natural.
/// DDL: <c>163-HU14071-permiso-persona-natural-enforced.sql</c>. Lleva los atributos inline (sin Designer): sin ellos la
/// migración no se descubre y no corre al arrancar.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261028130000_HU14071_PermisoPersonaNatural")]
public partial class HU14071_PermisoPersonaNatural : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("163-HU14071-permiso-persona-natural-enforced.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DELETE FROM security.role_permissions rp USING security.permissions p
             WHERE rp.permission_id = p.id AND p.slug = 'platform.persona_natural.manage';
            DELETE FROM security.permissions WHERE slug = 'platform.persona_natural.manage';
            DELETE FROM security.modules WHERE code = 'persona-natural-admin';
            """);
}
