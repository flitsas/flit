using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <summary>
    /// Un usuario tiene UN rol; lo que define lo que puede hacer son los permisos de ese rol.
    /// Revierte el modelo aditivo de la HU #10506 por decisión del responsable funcional.
    ///
    /// <para>El DDL embebido (<c>55-rol-unico-por-usuario.sql</c>) cierra en soft-delete las
    /// asignaciones sobrantes —conservando la más reciente por (usuario, tenant)— y restituye el
    /// índice único <c>uq_ura_active_user_tenant</c> que la HU #10506 había tumbado. Hace lo
    /// mismo con <c>security.invitation_roles</c>.</para>
    ///
    /// <para><b>Toca datos existentes:</b> un usuario con dos roles se queda con el último que le
    /// asignaron y pierde los permisos del otro. El soft-delete conserva el histórico, así que se
    /// puede revertir fila a fila si hiciera falta.</para>
    /// </summary>
    public partial class RolUnicoPorUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("55-rol-unico-por-usuario.sql"));
        }

        /// <inheritdoc />
        /// <remarks>
        /// Solo devuelve los índices al estado de la HU #10506: las asignaciones cerradas NO se
        /// reabren, porque no hay forma de distinguir las que cerró esta migración de las que
        /// cerró un administrador.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS security.uq_invitation_roles_single;
                DROP INDEX IF EXISTS security.uq_ura_active_user_tenant;
                CREATE UNIQUE INDEX IF NOT EXISTS uq_ura_active_user_tenant_role
                    ON security.user_role_assignments(user_id, tenant_id, role_id)
                    WHERE deleted_at IS NULL;
                """);
        }
    }
}
