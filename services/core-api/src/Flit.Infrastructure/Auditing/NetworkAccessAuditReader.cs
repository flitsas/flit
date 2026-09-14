using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Auditing;

/// <summary>
/// HU #12361 — lectura de <c>tramites.network_access_audit</c> para la consulta del hijo
/// (<c>/tramites/network-access-audit/mine</c>) y del SuperAdmin (<c>/admin/platform/network-access-audit</c>).
/// Con <see cref="NetworkAccessAuditQuery.TenantId"/> devuelve las filas cuyo trámite pertenece a ese
/// hijo o que lo alcanzaron en un listado/estadística (<c>reached_tenant_ids</c>). Orden: más reciente
/// primero. Solo identificadores en la salida.
/// </summary>
internal sealed class NetworkAccessAuditReader(FlitDbContext db) : INetworkAccessAuditReader
{
    public async Task<(IReadOnlyList<NetworkAccessAuditRow> Items, int Total)> SearchAsync(
        NetworkAccessAuditQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var rows = db.NetworkAccessAuditEntries.AsNoTracking();

        if (query.TenantId is { } tenant && tenant != Guid.Empty)
            rows = rows.Where(x => x.ProcedureTenantId == tenant || x.ReachedTenantIds.Contains(tenant));
        if (query.From is { } from)
            rows = rows.Where(x => x.OccurredAt >= from);
        if (query.To is { } to)
            rows = rows.Where(x => x.OccurredAt <= to);
        if (!string.IsNullOrWhiteSpace(query.Resource))
            rows = rows.Where(x => x.Resource == query.Resource);

        var total = await rows.CountAsync(cancellationToken).ConfigureAwait(false);
        var page = await rows
            .OrderByDescending(x => x.OccurredAt)
            .ThenByDescending(x => x.Id)
            .Skip((query.EffectivePage - 1) * query.EffectiveTake)
            .Take(query.EffectiveTake)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = page.Select(x => new NetworkAccessAuditRow(
            x.Id,
            x.OccurredAt,
            x.ActorUserId,
            x.ActorTenantId,
            x.ReachedTenantIds,
            x.Resource,
            x.ProcedureId,
            x.ProcedureTenantId,
            x.AttachmentId,
            x.Result)).ToList();

        return (items, total);
    }
}
