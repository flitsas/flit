using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #13809 (Feature #13808, Épica #13516) — correo normalizado y único a nivel de datos: normaliza usuarios e
/// invitaciones, CHECK de normalización en ambas tablas e invitación pendiente única por correo en toda la
/// plataforma. Aborta si hay usuarios vivos duplicados ignorando mayúsculas.
/// DDL: <c>149-HU13809-unicidad-correo-normalizado.sql</c>. No cambia el modelo EF (uq_users_email conserva su
/// definición; el CHECK y el índice parcial de invitaciones viven solo en la DDL).
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261022100000_HU13809_UnicidadCorreoNormalizado")]
public partial class HU13809_UnicidadCorreoNormalizado : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("149-HU13809-unicidad-correo-normalizado.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Restaura la unicidad de pendientes por compañía y retira los CHECK. No devuelve los correos a su forma
    /// original ni reabre las invitaciones canceladas: esa información no se conserva.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS security.uq_user_invitations_email_pending;
            CREATE UNIQUE INDEX IF NOT EXISTS uq_user_invitations_tenant_email_pending
                ON security.user_invitations (tenant_id, email) WHERE status = 'pending';
            ALTER TABLE security.user_invitations DROP CONSTRAINT IF EXISTS ck_user_invitations_email_normalizado;
            ALTER TABLE identity.users DROP CONSTRAINT IF EXISTS ck_users_email_normalizado;
            """);
}
