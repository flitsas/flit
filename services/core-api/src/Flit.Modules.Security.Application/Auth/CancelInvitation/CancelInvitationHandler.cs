using Flit.Modules.Security.Domain.Auth;

namespace Flit.Modules.Security.Application.Auth.CancelInvitation;

/// <summary>
/// HU #10627 — Cancelar invitación pendiente. Distinto de reenviar (HU #10625): anula la
/// invitación en vez de reenviar el correo. Al cancelar, el enlace de activación anterior deja
/// de funcionar (la invitación deja de estar "pending") y el email queda disponible de nuevo
/// para una nueva invitación (no queda bloqueado — <c>ExistsPendingAsync</c> ya filtra por
/// <c>Status = "pending"</c>).
/// </summary>
public sealed class CancelInvitationHandler(IInvitationRepository invitationRepository)
{
    public async Task HandleAsync(CancelInvitationCommand command, CancellationToken cancellationToken)
    {
        var invitation = await invitationRepository.FindByIdAsync(command.InvitationId, cancellationToken);

        // Mismo error para "no existe" y "existe pero fuera del alcance del caller" — no revela
        // la existencia de invitaciones de otros tenants (mapeado a 404 en el endpoint).
        if (invitation is null || (command.ScopeTenantId.HasValue && invitation.TenantId != command.ScopeTenantId.Value))
            throw new InvitationNotFoundException();

        // AC2 — solo se puede cancelar una invitación en estado "pending"; si ya fue aceptada o
        // previamente cancelada, es un error de negocio explícito (no un no-op silencioso).
        if (invitation.Status != "pending")
            throw new InvitationNotPendingException();

        await invitationRepository.CancelAsync(command.InvitationId, command.CancelledBy, cancellationToken);
    }
}
