namespace Flit.Modules.Security.Domain.Auth;

/// <summary>Rol activo de un usuario, tal como se emite en el JWT (HU #10506, multi-rol).</summary>
public sealed record UserRoleSnapshot(Guid Id, string Code);

public sealed class UserAuthSnapshot
{
    public Guid UserId { get; init; }

    public string Email { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string PasswordHash { get; init; } = string.Empty;

    public bool MustChangePassword { get; init; }

    public Guid TenantId { get; init; }

    /// <summary>
    /// Roles ACTIVOS del usuario en <see cref="TenantId"/> (HU #10506: soporte multi-rol).
    /// Solo incluye asignaciones cuyo <c>Role.IsActive == true</c> y <c>Role.DeletedAt == null</c>.
    /// Puede estar vacía si el usuario no tiene ninguna asignación activa, o si tuvo asignaciones
    /// pero todos sus roles fueron desactivados (HU #10507: bloqueo de login, ver
    /// <see cref="TotalAssignedRolesCount"/> para distinguir ambos casos).
    /// </summary>
    public IReadOnlyList<UserRoleSnapshot> ActiveRoles { get; init; } = [];

    /// <summary>
    /// Cantidad TOTAL de asignaciones de rol que el usuario tuvo alguna vez activas
    /// (<c>UserRoleAssignment.DeletedAt == null</c>), SIN filtrar por si el <c>Role</c> referenciado
    /// sigue activo en el catálogo global. Distinto de <see cref="ActiveRoles"/>.Count (HU #10507):
    /// si este valor es 0, el usuario nunca tuvo rol asignado y el login debe proceder con
    /// normalidad (AC3); si es mayor a 0 pero <see cref="ActiveRoles"/> está vacía, todos los roles
    /// del usuario fueron desactivados y el login debe bloquearse (AC2).
    /// </summary>
    public int TotalAssignedRolesCount { get; init; }

    public IReadOnlyList<string> PermissionSlugs { get; init; } = [];

    public string TenantName { get; init; } = string.Empty;

    public bool IsTemporarilySuspended { get; init; }
}
