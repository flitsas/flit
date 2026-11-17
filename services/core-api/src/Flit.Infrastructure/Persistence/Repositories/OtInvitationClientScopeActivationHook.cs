using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Al activarse la cuenta de un usuario invitado a un OT con alcance 'especificos' (compañías guardadas en la invitación,
/// <c>admin.ot_invitation_client_scope_clients</c>), crea su <c>admin.ot_user_client_scopes</c> en el MISMO SaveChanges que el
/// alta del usuario: o quedan los dos o ninguno, así que no hay una ventana en la que el usuario exista sin restricción.
/// Invitaciones sin filas (alcance 'todos', las de siempre) no hacen nada.
/// </summary>
internal sealed class OtInvitationClientScopeActivationHook(FlitDbContext db) : IUserActivationHook
{
    public async Task OnUserCreatedAsync(ActivationData data, Guid userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);

        var clientIds = await db.OtInvitationClientScopeClients.AsNoTracking()
            .Where(c => c.InvitationId == data.InvitationId)
            .Select(c => c.ClientTenantId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var officeId = await db.TransitOfficeProfiles.AsNoTracking()
            .Where(p => p.TenantId == data.TenantId)
            .Select(p => (Guid?)p.TransitOfficeId)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (officeId is null)
        {
            // Un tenant que no es OT no tiene alcance por compañías que aplicar.
            return;
        }

        if (clientIds.Count == 0)
        {
            // Red de seguridad: un invitador restringido ('especificos') no puede dejar a nadie en 'todos'. Si su invitación
            // llegó sin compañías (p. ej. falló guardarlas tras crearla), el invitado hereda el alcance ACTUAL del invitador.
            var inviterScope = await OtUserClientScopeReader
                .ReadAsync(db, data.InvitedBy, officeId.Value, cancellationToken).ConfigureAwait(false);
            if (inviterScope is not { Count: > 0 })
            {
                return;
            }

            clientIds = [.. inviterScope];
        }

        var scopeId = Guid.CreateVersion7();
        db.OtUserClientScopes.Add(new OtUserClientScopeEntity
        {
            Id = scopeId,
            TenantId = data.TenantId,
            UserId = userId,
            TransitOfficeId = officeId.Value,
            Mode = OtUserClientScopeModes.Especificos,
            CreatedAt = data.ActivatedAt,
            CreatedBy = data.InvitedBy,
            UpdatedAt = data.ActivatedAt,
            UpdatedBy = data.InvitedBy,
            Clients =
            [
                .. clientIds.Select(id => new OtUserClientScopeClientEntity
                {
                    ScopeId = scopeId,
                    ClientTenantId = id,
                    CreatedAt = data.ActivatedAt,
                    CreatedBy = data.InvitedBy,
                }),
            ],
        });
    }
}
