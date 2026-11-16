namespace Flit.Modules.Security.Domain.UserRoles.ProductRoles;

/// <summary>
/// ADR-0089 (HU #14587) — puerto de las operaciones por producto sobre <c>security.user_role_assignments</c> y
/// <c>security.product_role_suspensions</c>. Un usuario tiene un rol activo por empresa y producto
/// (<c>uq_ura_active_user_tenant_product</c>); estas operaciones solo tocan las asignaciones del producto indicado y
/// conservan las de los demás (a diferencia de <c>AssignRoleHandler</c>, que cierra todas las del tenant).
/// </summary>
public interface IProductRoleRepository
{
    /// <summary>Rol global (sin compañía), activo, de tipo COMPANY, con ese código y ese producto; <c>null</c> si no hay.</summary>
    Task<ProductRoleRef?> FindAssignableRoleAsync(string productCode, string roleCode, CancellationToken ct);

    /// <summary>
    /// El usuario como miembro de la empresa (asignación vigente en ella o <c>home_tenant_id</c>), con su rol vigente del
    /// producto y, si lo hay, el rol guardado en la marca de suspensión de producto. <c>null</c> si no existe, está borrado
    /// o no es de la empresa.
    /// </summary>
    Task<ProductMemberSnapshot?> GetMemberAsync(Guid tenantId, Guid userId, string productCode, CancellationToken ct);

    /// <summary>Invitación pendiente de la empresa con un rol del producto; <c>null</c> si no.</summary>
    Task<ProductInvitationSnapshot?> GetPendingInvitationAsync(Guid tenantId, Guid invitationId, string productCode, CancellationToken ct);

    /// <summary>Miembros de la empresa con rol vigente del producto o marcados como suspendidos en él.</summary>
    Task<IReadOnlyList<ProductMemberSnapshot>> ListMembersAsync(Guid tenantId, string productCode, CancellationToken ct);

    /// <summary>Invitaciones pendientes de la empresa con un rol del producto.</summary>
    Task<IReadOnlyList<ProductInvitationSnapshot>> ListPendingInvitationsAsync(Guid tenantId, string productCode, CancellationToken ct);

    /// <summary>Cuántos usuarios activos, distintos de <paramref name="excludingUserId"/>, tienen vigente el rol admin del producto.</summary>
    Task<int> CountOtherActiveAdminsAsync(Guid tenantId, string adminRoleCode, Guid excludingUserId, CancellationToken ct);

    /// <summary>En una transacción: cierra las asignaciones vigentes del usuario en la empresa de ESE producto y crea la nueva.</summary>
    Task ReplaceProductRoleAsync(Guid tenantId, Guid userId, string productCode, Guid roleId, Guid actorId, CancellationToken ct);

    /// <summary>Crea la asignación del producto a un miembro que no tenía rol en él (invitar a un miembro).</summary>
    Task AddProductRoleAsync(Guid tenantId, Guid userId, Guid roleId, Guid actorId, CancellationToken ct);

    /// <summary>En una transacción: cierra la asignación del producto y guarda la marca con el rol que tenía.</summary>
    Task SuspendAsync(Guid tenantId, Guid userId, string productCode, Guid roleId, Guid actorId, CancellationToken ct);

    /// <summary>En una transacción: recrea la asignación con <paramref name="roleId"/> y borra la marca.</summary>
    Task ReactivateAsync(Guid tenantId, Guid userId, string productCode, Guid roleId, Guid actorId, CancellationToken ct);

    /// <summary>Borra la marca de suspensión sin tocar asignaciones (el hub ya le dio otro rol del producto).</summary>
    Task ClearSuspensionAsync(Guid tenantId, Guid userId, string productCode, CancellationToken ct);

    /// <summary>A quién apunta un correo al invitar desde el producto (sin revelarlo hacia afuera).</summary>
    Task<InviteTarget> ResolveInviteTargetAsync(Guid tenantId, string normalizedEmail, string productCode, CancellationToken ct);

    /// <summary>Conteos por empresa del producto (usuarios = activos + suspendidos + invitados; admins = activos con el rol admin).</summary>
    Task<IReadOnlyList<ProductTenantCount>> CountByTenantAsync(string productCode, string adminRoleCode, CancellationToken ct);
}

/// <summary>Rol del catálogo, por id y código.</summary>
public sealed record ProductRoleRef(Guid Id, string Code);

/// <param name="AccountActive">Cuenta activa en la plataforma y sin suspensión temporal vigente en la empresa.</param>
/// <param name="IsAdminCompany">Tiene AdminCompany vigente en la empresa: su admin del producto es espejo y solo cambia en el hub.</param>
/// <param name="CurrentRole">Rol vigente del producto, o <c>null</c>.</param>
/// <param name="SuspendedRole">Rol guardado en la marca de suspensión de producto, o <c>null</c>.</param>
public sealed record ProductMemberSnapshot(
    Guid UserId,
    string DisplayName,
    string Email,
    bool AccountActive,
    bool IsAdminCompany,
    bool IsSuperAdmin,
    ProductRoleRef? CurrentRole,
    ProductRoleRef? SuspendedRole);

public sealed record ProductInvitationSnapshot(Guid InvitationId, string FullName, string Email, string RoleCode);

public enum InviteTargetKind
{
    /// <summary>Ningún usuario ni invitación pendiente con ese correo.</summary>
    NewEmail,

    /// <summary>Ya hay una invitación pendiente con ese correo (en cualquier empresa).</summary>
    PendingInvitation,

    /// <summary>Miembro de la empresa sin rol del producto (ni vigente ni suspendido).</summary>
    MemberWithoutRole,

    /// <summary>Miembro de la empresa con rol del producto vigente o suspendido.</summary>
    MemberWithRole,

    /// <summary>El correo es de un usuario de otra empresa: nunca se mueve.</summary>
    OtherTenant,
}

public sealed record InviteTarget(InviteTargetKind Kind, Guid? UserId = null);

public sealed record ProductTenantCount(Guid TenantId, int Users, int Admins);
