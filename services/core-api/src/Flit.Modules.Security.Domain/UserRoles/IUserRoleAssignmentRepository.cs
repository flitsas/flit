namespace Flit.Modules.Security.Domain.UserRoles;

public interface IUserRoleAssignmentRepository
{
    Task<bool> UserBelongsToTenantAsync(Guid userId, Guid tenantId, CancellationToken ct);

    /// <summary>
    /// Tenant EFECTIVO del usuario: su <c>HomeTenantId</c> si lo tiene y, si no, el tenant de su
    /// asignación de rol activa más antigua (mismo criterio con que <c>AuthUserRepository</c>
    /// resuelve el tenant del JWT al iniciar sesión). <c>null</c> si el usuario no existe o no
    /// pertenece a ningún tenant. Lo usa el SuperAdmin, cuyo propio tenant es el interno de FLIT
    /// y por tanto nunca coincide con el del usuario que administra.
    /// </summary>
    Task<Guid?> GetUserTenantAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Devuelve el rol si existe, está activo y no borrado (catálogo global, HU #10505), junto
    /// con su <c>TargetEntityType</c> — o <c>null</c> si no aplica (HU #10506: la asignación
    /// valida que el rol sea compatible con el tipo de tenant destino).
    /// </summary>
    Task<RoleForAssignmentSnapshot?> GetActiveRoleAsync(Guid roleId, CancellationToken ct);

    /// <summary>
    /// Resuelve el tipo de entidad de negocio del tenant (<c>COMPANY</c> | <c>TRANSIT_OFFICE</c>)
    /// según tenga o no un <c>TransitOfficeProfile</c> asociado — mismo criterio ya usado en
    /// <c>SecurityEndpoints</c>/<c>AdminOtEndpoints</c> para resolver el rol de invitación.
    /// </summary>
    Task<string> GetTenantTargetEntityTypeAsync(Guid tenantId, CancellationToken ct);

    /// <summary>Asignación ACTIVA puntual de (userId, tenantId, roleId), o <c>null</c> si no existe.</summary>
    Task<UserRoleAssignmentSnapshot?> GetActiveAssignmentAsync(Guid userId, Guid tenantId, Guid roleId, CancellationToken ct);

    /// <summary>Todas las asignaciones ACTIVAS del usuario en el tenant (modelo aditivo multi-rol, HU #10506).</summary>
    Task<IReadOnlyList<UserRoleAssignmentSnapshot>> GetActiveAssignmentsAsync(Guid userId, Guid tenantId, CancellationToken ct);

    Task SoftDeleteAssignmentAsync(Guid assignmentId, Guid deletedBy, CancellationToken ct);

    Task<Guid> CreateAssignmentAsync(AssignRoleData data, CancellationToken ct);
}

public sealed record AssignRoleData(Guid TenantId, Guid UserId, Guid RoleId, Guid AssignedBy);

public sealed record UserRoleAssignmentSnapshot(Guid Id, Guid UserId, Guid RoleId);

public sealed record RoleForAssignmentSnapshot(Guid Id, string TargetEntityType);
