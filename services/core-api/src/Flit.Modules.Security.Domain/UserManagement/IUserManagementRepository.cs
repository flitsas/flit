namespace Flit.Modules.Security.Domain.UserManagement;

/// <summary>
/// Repositorio compartido de suspensión/desactivación/reactivación de usuarios (HU #10619),
/// consumido por <c>SuspendUserHandler</c>/<c>UnsuspendUserHandler</c> desde ambos endpoints
/// (<c>SecurityEndpoints</c> AdminCompany/SuperAdmin y <c>AdminOtEndpoints</c> ot_admin/SuperAdmin)
/// para eliminar la duplicación de lógica que manipulaba <c>FlitDbContext</c> directamente.
/// </summary>
public interface IUserManagementRepository
{
    /// <summary>
    /// Resuelve el usuario objetivo de una acción administrativa y su tenant "hogar"
    /// (<c>User.HomeTenantId</c> — mismo criterio que <c>UserRoleAssignmentRepository.UserBelongsToTenantAsync</c>).
    /// </summary>
    Task<UserManagementTarget?> FindTargetAsync(Guid userId, bool includeDeleted, CancellationToken ct);

    /// <summary>
    /// Todas las asignaciones ACTIVAS del usuario cuyo código de rol es uno de los roles
    /// administrativos (<see cref="AdminRoleCodes"/>) — usadas por la guarda de "último admin".
    /// </summary>
    Task<IReadOnlyList<ActiveAdminRoleAssignment>> GetActiveAdminRoleAssignmentsAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Indica si existe al menos OTRO usuario (distinto de <paramref name="excludingUserId"/>) con
    /// una asignación ACTIVA del rol <paramref name="roleCode"/> que a su vez NO esté él mismo
    /// suspendido (temporal o indefinidamente). <paramref name="scopeTenantId"/> nulo = alcance
    /// GLOBAL (rol <c>SuperAdmin</c>); no nulo = filtra por ese tenant (<c>AdminCompany</c>/<c>ot_admin</c>).
    /// </summary>
    Task<bool> HasOtherActiveAdminsAsync(string roleCode, Guid? scopeTenantId, Guid excludingUserId, CancellationToken ct);

    /// <summary>
    /// Soft-delete de cualquier suspensión ACTIVA del usuario en el tenant (temporal vigente o
    /// indefinida). Devuelve la cantidad de filas cerradas (0 = no había ninguna activa).
    /// </summary>
    Task<int> CloseActiveSuspensionsAsync(Guid tenantId, Guid userId, DateTimeOffset now, Guid? closedBy, CancellationToken ct);

    /// <summary>
    /// Crea una nueva suspensión y devuelve su Id. <paramref name="endsAt"/> nulo = desactivación
    /// indefinida (HU #10619 AC1); con valor = suspensión temporal que se reactiva sola al vencer (AC2).
    /// </summary>
    Task<Guid> CreateSuspensionAsync(
        Guid tenantId,
        Guid userId,
        DateTimeOffset startsAt,
        DateTimeOffset? endsAt,
        string reason,
        Guid? createdBy,
        CancellationToken ct);
}

/// <summary>Snapshot mínimo del usuario objetivo de una acción de suspensión/reactivación.</summary>
public sealed record UserManagementTarget(Guid UserId, Guid TenantId, string Email, string DisplayName, DateTimeOffset? DeletedAt);

/// <summary>Asignación activa de un rol administrativo, con el tenant en el que aplica.</summary>
public sealed record ActiveAdminRoleAssignment(string RoleCode, Guid TenantId);

/// <summary>
/// Códigos de rol con privilegios administrativos, para la guarda de "último administrador
/// activo" (HU #10619 AC4). Deben coincidir EXACTAMENTE con los valores usados en
/// <c>Flit.Api.Authorization.AdminAuthorization</c> (capa API no referenciable desde Domain).
/// </summary>
public static class AdminRoleCodes
{
    public const string SuperAdmin = "SuperAdmin";

    public const string AdminCompany = "AdminCompany";

    public const string OtAdmin = "ot_admin";

    public static readonly IReadOnlyCollection<string> All = [SuperAdmin, AdminCompany, OtAdmin];
}
