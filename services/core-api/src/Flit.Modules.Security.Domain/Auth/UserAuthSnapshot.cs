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
    /// Puede estar vacía si el usuario no tiene ninguna asignación activa — el bloqueo de login
    /// por "todos los roles inactivos" es HU #10507, fuera de alcance aquí.
    /// </summary>
    public IReadOnlyList<UserRoleSnapshot> ActiveRoles { get; init; } = [];

    public IReadOnlyList<string> PermissionSlugs { get; init; } = [];

    public string TenantName { get; init; } = string.Empty;

    public bool IsTemporarilySuspended { get; init; }
}
