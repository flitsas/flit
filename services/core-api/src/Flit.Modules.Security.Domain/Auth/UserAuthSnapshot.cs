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

    /// <summary>
    /// NIT (<c>identity.tenants.tax_id</c>) del tenant asociado (HU #10616, AC1/AC2). Puede venir
    /// vacío si el tenant no tiene NIT registrado (AC4) — el login debe completarse igual, sin
    /// romper la emisión del JWT.
    /// </summary>
    public string TenantTaxId { get; init; } = string.Empty;

    /// <summary>
    /// Tipo de entidad de negocio del tenant: <c>"COMPANY"</c> u <c>"TRANSIT_OFFICE"</c> (HU #10616,
    /// AC1/AC2). Mismo criterio ya usado por <see cref="Flit.Modules.Security.Domain.UserRoles.IUserRoleAssignmentRepository.GetTenantTargetEntityTypeAsync"/>:
    /// un tenant con <c>TransitOfficeProfile</c> asociado es <c>TRANSIT_OFFICE</c>; el resto, <c>COMPANY</c>.
    /// </summary>
    public string EntityType { get; init; } = string.Empty;

    public bool IsTemporarilySuspended { get; init; }
}
