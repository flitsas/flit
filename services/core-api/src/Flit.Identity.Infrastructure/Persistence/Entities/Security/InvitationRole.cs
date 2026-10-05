namespace Flit.Infrastructure.Persistence.Entities.Security;

/// <summary>
/// Tabla puente N:M entre <see cref="UserInvitation"/> y <see cref="Role"/> (HU #10506 AC4):
/// una invitación puede tener varios roles simultáneos. <see cref="UserInvitation.RoleId"/> se
/// conserva como el rol "primario" (el primero seleccionado, para no romper consumidores que
/// aún leen ese único campo); esta tabla es la fuente completa de N roles por invitación.
/// Hereda <see cref="Entities.Common.TenantAuditableEntity"/> (a diferencia de
/// <c>RoleGrant</c>/<c>role_permissions</c>, que son catálogo GLOBAL desde HU #10505): esta
/// fila SÍ es un hecho de negocio propio del tenant de la invitación (RLS + auditoría estándar).
/// </summary>
public sealed class InvitationRole : Entities.Common.TenantAuditableEntity
{
    public Guid InvitationId { get; set; }

    public Guid RoleId { get; set; }

    public UserInvitation Invitation { get; set; } = null!;

    public Role Role { get; set; } = null!;
}
