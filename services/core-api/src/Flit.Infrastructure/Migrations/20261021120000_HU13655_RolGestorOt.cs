using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #13655 (Feature #13104 F-D2, Épica #12753) — rol de sistema «Gestor OT» (<c>gestor_tramites_ot</c>,
/// TRANSIT_OFFICE, producto trámites) con <c>ot.tramites.manage</c> y <c>ot.reportes.manage</c>.
/// DDL: <c>147-HU13655-rol-gestor-ot.sql</c>. Solo datos.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261021120000_HU13655_RolGestorOt")]
public partial class HU13655_RolGestorOt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("147-HU13655-rol-gestor-ot.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Quita el rol global solo si ningún usuario ni invitación lo usa (las FK son RESTRICT); sus permisos se van en
    /// cascada. Si alguien lo tiene, se conserva: bajar la migración no deja usuarios sin rol.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DELETE FROM security.roles r
             WHERE r.code = 'gestor_tramites_ot' AND r.target_entity_type = 'TRANSIT_OFFICE' AND r.tenant_id IS NULL
               AND NOT EXISTS (SELECT 1 FROM security.user_role_assignments a WHERE a.role_id = r.id)
               AND NOT EXISTS (SELECT 1 FROM security.user_invitations i WHERE i.role_id = r.id)
               AND NOT EXISTS (SELECT 1 FROM security.invitation_roles ir WHERE ir.role_id = r.id);
            """);
}
