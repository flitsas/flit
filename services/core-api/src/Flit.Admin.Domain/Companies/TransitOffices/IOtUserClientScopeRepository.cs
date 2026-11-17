namespace Flit.Admin.Domain.Companies.TransitOffices;

/// <summary>Compañía cliente visible para un organismo de tránsito (selector del alcance de usuarios).</summary>
public sealed record OtClientCompanyItem(Guid TenantId, string Name, string? Nit);

/// <summary>Alcance de compañías cliente de un usuario del OT (<c>todos</c> | <c>especificos</c>).</summary>
public sealed record OtUserClientScopeView(string Mode, IReadOnlyList<OtClientCompanyItem> Clients);

/// <summary>Resumen del alcance para el listado de usuarios.</summary>
public sealed record OtUserClientScopeSummary(string Mode, int ClientCount);

/// <summary>Valores del campo <c>mode</c> del alcance.</summary>
public static class OtClientScopeModes
{
    public const string Todos = "todos";
    public const string Especificos = "especificos";

    public static bool IsValid(string? mode) => mode is Todos or Especificos;
}

/// <summary>
/// Administración del alcance de compañías cliente por usuario de un organismo de tránsito. La LECTURA para
/// filtrar datos vive en <see cref="IOtUserClientScopeResolver"/>; esto es solo el mantenimiento (Admin OT / Super Admin).
/// </summary>
public interface IOtUserClientScopeRepository
{
    /// <summary>
    /// Compañías cliente que el organismo ve por nombre (la misma definición que usan consultas y métricas), con su NIT.
    /// Sin organismo resoluble, lista vacía.
    /// </summary>
    Task<IReadOnlyList<OtClientCompanyItem>> ListClientCompaniesAsync(
        Guid otTenantId, Guid? transitOfficeIdOverride, CancellationToken cancellationToken = default);

    /// <summary>Organismo de un usuario con cuenta en el tenant OT (null si el usuario no es de ese OT).</summary>
    Task<Guid?> ResolveTransitOfficeIdOfUserAsync(
        Guid otTenantId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Alcance vigente del usuario (ausencia de fila = todos).</summary>
    Task<OtUserClientScopeView> GetAsync(
        Guid otTenantId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Resumen por usuario (usuarios con fila 'especificos'); el resto es 'todos'. Clave: user id.</summary>
    Task<IReadOnlyDictionary<Guid, OtUserClientScopeSummary>> GetSummariesAsync(
        Guid otTenantId, CancellationToken cancellationToken = default);

    /// <summary>Resumen del alcance guardado en invitaciones pendientes. Clave: invitation id.</summary>
    Task<IReadOnlyDictionary<Guid, OtUserClientScopeSummary>> GetInvitationSummariesAsync(
        Guid otTenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reemplaza el alcance del usuario y deja el rastro en la auditoría de permisos, en una sola transacción.
    /// <paramref name="clientTenantIds"/> se ignora con <c>todos</c> (se borra la lista).
    /// </summary>
    Task SetAsync(
        Guid otTenantId,
        Guid transitOfficeId,
        Guid userId,
        string mode,
        IReadOnlyCollection<Guid> clientTenantIds,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Guarda el alcance de una invitación recién creada; se materializa al aceptarla (el usuario aún no existe).
    /// </summary>
    Task SetInvitationScopeAsync(
        Guid invitationId, IReadOnlyCollection<Guid> clientTenantIds, Guid actorUserId,
        CancellationToken cancellationToken = default);
}
