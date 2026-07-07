namespace Flit.Modules.Security.Domain.UserManagement;

/// <summary>
/// Repositorio de gestión administrativa del perfil de un usuario (HU #10621): edición de
/// nombre y correo desde el panel de un tenant. Vive en su propio namespace
/// <c>UserManagement</c> para no acoplarse a <c>Auth</c> (login/recuperación de contraseña) ni
/// a <c>UserRoles</c> (asignación de roles).
/// </summary>
public interface IUserManagementRepository
{
    /// <summary>
    /// Snapshot del usuario objetivo de una edición administrativa, incluyendo su
    /// <c>RowVersion</c> para el chequeo de concurrencia optimista (AC4). Con
    /// <paramref name="includeDeleted"/> en <c>false</c> (uso normal del handler) un usuario
    /// soft-deleted se resuelve como <c>null</c> (no encontrado) para el caller.
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
}

/// <summary>Snapshot de lectura del usuario objetivo de una edición administrativa (HU #10621).</summary>
public sealed record UserManagementTarget(
    Guid UserId,
    Guid TenantId,
    string Email,
    string DisplayName,
    DateTimeOffset? DeletedAt,
    long RowVersion);

/// <summary>Resultado de la búsqueda de un usuario existente por correo (HU #10621 AC2/AC3).</summary>
public sealed record ExistingUserByEmail(Guid UserId, bool IsDeleted);
