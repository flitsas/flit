namespace Flit.Modules.Security.Domain.Auth;

public interface IInvitationRepository
{
    Task<bool> ExistsPendingAsync(Guid tenantId, string email, CancellationToken cancellationToken);

    Task<bool> UserExistsWithEmailAsync(string email, CancellationToken cancellationToken);

    Task<bool> RoleExistsInTenantAsync(Guid tenantId, Guid roleId, CancellationToken cancellationToken);

    Task<Guid> CreateAsync(UserInvitationData invitation, CancellationToken cancellationToken);

    Task<PendingInvitation?> FindPendingByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    Task<IReadOnlyList<PendingInvitationSummary>> ListPendingByTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// HU #10625: busca una invitación por id para reenviarla. Si <paramref name="scopeTenantId"/>
    /// no es <c>null</c> (caller AdminCompany/ot_admin), la búsqueda se restringe a ese tenant;
    /// si es <c>null</c> (caller SuperAdmin), no hay restricción de tenant. Devuelve la invitación
    /// sin importar su <c>Status</c> — es responsabilidad del handler decidir si está pendiente.
    /// </summary>
    Task<InvitationForResend?> FindForResendAsync(
        Guid invitationId, Guid? scopeTenantId, CancellationToken cancellationToken);

    /// <summary>
    /// HU #10625: persiste el nuevo hash de token y <c>LastSentAt</c> de un reenvío exitoso.
    /// </summary>
    Task UpdateResendAsync(
        Guid invitationId, string tokenHash, DateTimeOffset lastSentAt, Guid resentBy, CancellationToken cancellationToken);

    /// <summary>Estado y tenant actuales de una invitación por Id (HU #10627), sin filtrar por
    /// estado — a diferencia de <see cref="FindPendingByTokenHashAsync"/>, debe devolver también
    /// invitaciones ya aceptadas o canceladas para que el handler distinga "no existe / fuera de
    /// alcance" (404) de "ya no está pendiente" (409).</summary>
    Task<InvitationStatusInfo?> FindByIdAsync(Guid invitationId, CancellationToken cancellationToken);

    /// <summary>
    /// Marca la invitación como cancelada (HU #10627 AC1, redefinido por ADR-0048): <c>Status =
    /// "cancelled"</c>. YA NO marca <c>DeletedAt</c>/<c>DeletedBy</c> — <c>cancelled</c> es un
    /// estado de negocio vivo y reversible (ver <see cref="ReactivateAsync"/>), no un soft-delete;
    /// dejar <c>DeletedAt</c> poblado en una fila visible y reactivable sería un estado imposible
    /// que confundiría a cualquier query futura con el criterio estándar <c>DeletedAt == null</c>.
    /// El enlace de activación deja de resolver (ya no está "pending") y el email queda
    /// disponible para una nueva invitación.
    /// </summary>
    Task CancelAsync(Guid invitationId, Guid cancelledBy, CancellationToken cancellationToken);

    /// <summary>
    /// HU #11552 / ADR-0048: busca una invitación por id para reactivarla, sin filtrar por
    /// estado (el handler decide 404 por fuera de alcance vs 409 por no estar "cancelled").
    /// <paramref name="scopeTenantId"/> replica el mismo patrón de alcance que
    /// <see cref="FindForResendAsync"/>: <c>null</c> (SuperAdmin) no restringe por tenant.
    /// Incluye los roles vigentes de la invitación (tabla puente <c>invitation_roles</c>) para
    /// que el handler pueda validar que siguen activos antes de reactivar.
    /// </summary>
    Task<InvitationForReactivate?> FindForReactivateAsync(
        Guid invitationId, Guid? scopeTenantId, CancellationToken cancellationToken);

    /// <summary>
    /// HU #11552 / ADR-0048: revive una invitación cancelada a <c>pending</c> con un token
    /// SIEMPRE nuevo (nunca reutiliza <c>TokenHash</c>) y actualiza <c>LastSentAt</c> como un
    /// reenvío (comparte el cooldown anti-abuso con <c>ResendAsync</c>). El índice único parcial
    /// <c>uq_user_invitations_tenant_email_pending</c> es el guardarraíl duro: si otra invitación
    /// del mismo (tenant, email) ya está "pending", el <c>UPDATE</c> revienta con 23505 y la
    /// implementación lo traduce a <see cref="InvitationAlreadyPendingException"/> — el handler
    /// ya pre-valida con <c>ExistsPendingAsync</c>, esto es la red de la condición de carrera.
    /// </summary>
    Task ReactivateAsync(
        Guid invitationId,
        string tokenHash,
        DateTimeOffset reactivatedAt,
        Guid reactivatedBy,
        CancellationToken cancellationToken);
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

/// <summary>
/// HU #10625: datos mínimos para decidir y ejecutar el reenvío de una invitación.
/// </summary>
/// <param name="TenantId">
/// HU #11358 — tenant PROPIETARIO de la invitación (no el <c>ScopeTenantId</c> del caller, que
/// puede ser null para SuperAdmin): una invitación siempre pertenece a un tenant, así que este
/// campo es el que viaja como <see cref="EmailMessage.TenantId"/> al reenviar.
/// </param>
public sealed record InvitationForResend(
    Guid InvitationId,
    Guid TenantId,
    string Email,
    string FullName,
    string Status,
    DateTimeOffset? LastSentAt);

/// <summary>HU #10627: proyección mínima para validar alcance (tenant) y estado antes de cancelar.</summary>
public sealed record InvitationStatusInfo(
    Guid InvitationId,
    Guid TenantId,
    string Status);

/// <summary>
/// HU #11552 / ADR-0048: datos mínimos para decidir y ejecutar la reactivación de una invitación
/// cancelada. <c>RoleIds</c> viene de la tabla puente <c>invitation_roles</c> — el handler valida
/// que cada uno siga activo antes de reactivar (una invitación no puede resucitar con un rol que
/// ya no existe).
/// </summary>
public sealed record InvitationForReactivate(
    Guid InvitationId,
    Guid TenantId,
    string Email,
    string FullName,
    string Status,
    DateTimeOffset? LastSentAt,
    IReadOnlyList<Guid> RoleIds);
