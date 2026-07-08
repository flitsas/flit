namespace Flit.Modules.Security.Domain.Auth;

public interface IInvitationRepository
{
    Task<bool> ExistsPendingAsync(Guid tenantId, string email, CancellationToken cancellationToken);

    Task<bool> UserExistsWithEmailAsync(string email, CancellationToken cancellationToken);

    Task<bool> RoleExistsInTenantAsync(Guid tenantId, Guid roleId, CancellationToken cancellationToken);

    Task<Guid> CreateAsync(UserInvitationData invitation, CancellationToken cancellationToken);

    Task<PendingInvitation?> FindPendingByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    Task<IReadOnlyList<PendingInvitationSummary>> ListPendingByTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Estado y tenant actuales de una invitación por Id (HU #10627), sin filtrar por
    /// estado — a diferencia de <see cref="FindPendingByTokenHashAsync"/>, debe devolver también
    /// invitaciones ya aceptadas o canceladas para que el handler distinga "no existe / fuera de
    /// alcance" (404) de "ya no está pendiente" (409).</summary>
    Task<InvitationStatusInfo?> FindByIdAsync(Guid invitationId, CancellationToken cancellationToken);

    /// <summary>Marca la invitación como cancelada (HU #10627 AC1): <c>Status = "cancelled"</c> +
    /// soft-delete (<c>DeletedAt</c>/<c>DeletedBy</c>), consistente con el patrón estándar de
    /// soft-delete del sistema. El enlace de activación deja de resolver (ya no está "pending")
    /// y el email queda disponible para una nueva invitación.</summary>
    Task CancelAsync(Guid invitationId, Guid cancelledBy, CancellationToken cancellationToken);
}

/// <summary>
/// HU #10506 AC4/AC5: <c>RoleIds</c> reemplaza el <c>RoleId?</c> nullable — siempre tiene al
/// menos un elemento (validado en <c>CreateInvitationHandler</c> antes de llegar aquí).
/// </summary>
public sealed record UserInvitationData(
    Guid TenantId,
    string Email,
    string FullName,
    IReadOnlyList<Guid> RoleIds,
    string TokenHash,
    Guid InvitedBy);

public sealed record PendingInvitation(
    Guid InvitationId,
    Guid TenantId,
    string Email,
    string FullName,
    IReadOnlyList<Guid> RoleIds,
    Guid InvitedBy);

public sealed record PendingInvitationSummary(
    Guid InvitationId,
    string Email,
    string FullName,
    DateTimeOffset CreatedAt);

/// <summary>HU #10627: proyección mínima para validar alcance (tenant) y estado antes de cancelar.</summary>
public sealed record InvitationStatusInfo(
    Guid InvitationId,
    Guid TenantId,
    string Status);
