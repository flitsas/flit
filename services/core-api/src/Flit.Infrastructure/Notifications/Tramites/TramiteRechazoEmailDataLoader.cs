using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Notifications.Tramites;

/// <summary>
/// Carga las causales del catálogo y la observación del último evento de rechazo
/// para el cuerpo de <c>tramites.rechazado</c>. Sin I/O en el composer.
/// </summary>
internal static class TramiteRechazoEmailDataLoader
{
    internal const string CausalRetirada = "(causal retirada)";

    public static async Task<(IReadOnlyList<string> Causales, string? Observacion)> LoadAsync(
        FlitDbContext db,
        Guid tenantId,
        Guid procedureInstanceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var last = await db.ProcedureInstanceStatusHistories
            .AsNoTracking()
            .Where(h => h.TenantId == tenantId
                && h.ProcedureInstanceId == procedureInstanceId
                && h.ToStatus == TramiteEstado.Rechazado)
            .OrderByDescending(h => h.ChangedAt)
            .Select(h => new { h.Id, h.Reason })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (last is null)
            return ([], null);

        var reasonIds = await db.ProcedureInstanceRejectionReasons
            .AsNoTracking()
            .Where(r => r.StatusHistoryId == last.Id)
            .Select(r => r.RejectionReasonId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (reasonIds.Count == 0)
            return ([], last.Reason);

        var catalog = await db.RejectionReasons
            .AsNoTracking()
            .Where(r => reasonIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Description })
            .ToDictionaryAsync(r => r.Id, r => r.Description, cancellationToken)
            .ConfigureAwait(false);

        var causales = reasonIds
            .Select(id => catalog.GetValueOrDefault(id, CausalRetirada))
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return (causales, last.Reason);
    }
}
