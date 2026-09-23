using Flit.Infrastructure.Persistence;
using Flit.Modules.Quipux.Domain.Envios;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Quipux;

/// <summary>
/// HU #12787 (AC2) — implementación de <see cref="IMaestroRadicadoLookup"/> sobre
/// <c>tramites.quipux_submissions</c>. Une Trámites y Quipux AQUÍ, en Infrastructure (que ya referencia
/// ambos), para que ninguno de los dos módulos Application dependa del otro.
///
/// <para>Criterio de «radicado» = HU #12791 (<c>OtClientProcedureRepository</c>): <c>RegisteredAt</c>
/// con valor y estado distinto de <c>fallido</c> (una submission fallida nunca radicó). Filtra por
/// tenant Y trámite explícitos: el aislamiento no descansa en el RLS (decorativo en este despliegue).</para>
/// </summary>
internal sealed class MaestroRadicadoLookup(FlitDbContext db) : IMaestroRadicadoLookup
{
    public Task<Guid?> AttachmentRadicadoAsync(Guid tenantId, Guid instanceId, CancellationToken ct = default) =>
        Radicadas(tenantId, instanceId)
            .OrderByDescending(q => q.RegisteredAt)
            .Select(q => (Guid?)q.AttachmentId)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlySet<Guid>> AttachmentsRadicadosAsync(
        Guid tenantId, Guid instanceId, CancellationToken ct = default)
    {
        var ids = await Radicadas(tenantId, instanceId)
            .Select(q => q.AttachmentId)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return ids.ToHashSet();
    }

    private IQueryable<QuipuxSubmission> Radicadas(Guid tenantId, Guid instanceId) =>
        db.QuipuxSubmissions
            .AsNoTracking()
            .Where(q => q.TenantId == tenantId
                && q.ProcedureInstanceId == instanceId
                && q.RegisteredAt != null
                && q.Status != QuipuxSubmissionEstado.Fallido);
}
