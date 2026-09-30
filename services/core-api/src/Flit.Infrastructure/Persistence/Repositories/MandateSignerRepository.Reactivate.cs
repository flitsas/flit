using System.Text.Json;
using Flit.Admin.Domain.Companies.MandateSigners;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Reactivación del mandatario (punto de extensión de la HU #13136): vuelve activo y recupera solo su organismo
/// primario; las compañías se reasignan con «Editar». Nunca reactiva a un mandatario eliminado.
/// </summary>
internal sealed partial class MandateSignerRepository
{
    private async Task<MandateSignerLifecycleResult> PersistReactivateAsync(
        ReactivateMandateSignerData data,
        CancellationToken cancellationToken)
    {
        var signer = await _context.MandateSigners
            .FirstOrDefaultAsync(s => s.Id == data.MandateSignerId, cancellationToken)
            .ConfigureAwait(false);

        // Idempotente: 404 si no existe, está eliminado (no se reactiva) o ya estaba activo.
        if (signer is null || signer.IsActive || signer.DeletedAt is not null)
        {
            return MandateSignerLifecycleResult.NotApplied;
        }

        var now = DateTimeOffset.UtcNow;
        signer.IsActive = true;
        signer.UpdatedAt = now;
        signer.UpdatedBy = data.ChangedBy;
        await RestaurarOrganismoPrimarioAsync(signer, now, cancellationToken).ConfigureAwait(false);

        AddAudit(
            data.OtTenantId,
            fieldName: "is_active",
            oldValue: JsonSerializer.Serialize(false),
            newValue: JsonSerializer.Serialize(true),
            changedAt: now,
            changedBy: data.ChangedBy,
            correlationId: data.CorrelationId);

        await SaveConTraduccionDeUnicidadAsync(cancellationToken).ConfigureAwait(false);
        return new MandateSignerLifecycleResult(
            true, MandateSignerReassignmentResult.None, [], [], 0, 0, RestoredFromSnapshot: false);
    }
}
