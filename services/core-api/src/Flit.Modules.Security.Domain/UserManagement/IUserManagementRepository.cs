namespace Flit.Modules.Security.Domain.UserManagement;

/// <summary>
/// Repositorio compartido de gestión administrativa del ciclo de vida de un usuario: edición de
/// perfil (HU #10621), suspensión/desactivación/reactivación (HU #10619) y, en HUs posteriores,
/// eliminación/restauración. Vive en su propio namespace <c>UserManagement</c> para no acoplarse
/// a <c>Auth</c> (login/recuperación de contraseña) ni a <c>UserRoles</c> (asignación de roles).
/// Consumido desde ambos endpoints (<c>SecurityEndpoints</c> AdminCompany/SuperAdmin y
/// <c>AdminOtEndpoints</c> ot_admin/SuperAdmin) para eliminar la duplicación de lógica que antes
/// manipulaba <c>FlitDbContext</c> directamente.
/// </summary>
public interface IUserManagementRepository
{
    /// <summary>
    /// Resuelve el usuario objetivo de una acción administrativa, su tenant "hogar"
    /// (<c>User.HomeTenantId</c> — mismo criterio que
    /// <c>UserRoleAssignmentRepository.UserBelongsToTenantAsync</c>) y su <c>RowVersion</c> para
    /// el chequeo de concurrencia optimista (HU #10621 AC4). Con <paramref name="includeDeleted"/>
    /// en <c>false</c> (uso normal de la mayoría de handlers) un usuario soft-deleted se resuelve
    /// como <c>null</c> (no encontrado) para el caller.
    /// </summary>
    Task<UserManagementTarget?> FindTargetAsync(Guid userId, bool includeDeleted, CancellationToken ct);

    /// <summary>
    /// Busca cualquier usuario (activo o soft-deleted) que tenga ese correo, comparación
    /// case-insensitive (HU #10621 AC2/AC3 — <c>uq_users_email</c> es un índice único GLOBAL,
    /// no parcial: un correo soft-deleted sigue "ocupado" en BD). <c>null</c> si el correo
    /// está libre.
    /// </summary>
    Task<ExistingUserByEmail?> FindByEmailIncludingDeletedAsync(string email, CancellationToken ct);

    /// <summary>
    /// Actualiza nombre y/o correo del usuario con concurrencia optimista contra
    /// <paramref name="expectedRowVersion"/> (AC4: la versión que el caller leyó al abrir el
    /// formulario, no la que el repositorio acaba de traer de BD). Si la fila cambió desde
    /// entonces, lanza <see cref="UserProfileConcurrencyException"/> sin aplicar el cambio.
    /// AC5: no invalida sesiones activas — solo afecta el próximo login del usuario (el JWT ya
    /// emitido conserva el email anterior hasta su expiración natural; decisión de alcance
    /// explícita, no hay nada que invalidar aquí).
    /// </summary>
    Task UpdateProfileAsync(
        Guid userId,
        string? displayName,
        string? email,
        long expectedRowVersion,
        DateTimeOffset updatedAt,
        Guid? updatedBy,
        CancellationToken ct);

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

/// <summary>Snapshot de lectura del usuario objetivo de una acción administrativa.</summary>
public sealed record UserManagementTarget(
    Guid UserId,
    Guid TenantId,
    string Email,
    string DisplayName,
    DateTimeOffset? DeletedAt,
    long RowVersion);

/// <summary>Resultado de la búsqueda de un usuario existente por correo (HU #10621 AC2/AC3).</summary>
public sealed record ExistingUserByEmail(Guid UserId, bool IsDeleted);

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
