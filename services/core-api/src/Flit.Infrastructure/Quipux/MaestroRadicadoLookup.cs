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
/// <para>Dos criterios distintos (re-review #12760, N1/N4):</para>
/// <list type="bullet">
///   <item><b>Fijo</b> (<see cref="AttachmentRadicadoAsync"/>): radicación VIGENTE, <c>RegisteredAt</c> con
///   valor y estado <c>registrado</c> o <c>aprobado</c>. Un <c>rechazado</c> no fija nada: la secretaría
///   devolvió ese documento y la re-radicación necesita un maestro nuevo.</item>
///   <item><b>Protegido</b> (<see cref="AttachmentsProtegidosAsync"/>): todo adjunto que la secretaría tiene
///   o puede estar recibiendo: <c>pendiente</c> (en proceso, aunque le falte <c>RegisteredAt</c>),
///   <c>registrado</c>, <c>aprobado</c> y <c>rechazado</c>. Solo <c>fallido</c> queda fuera (nunca
///   radicó).</item>
/// </list>
/// <para>Filtra por tenant Y trámite explícitos: el aislamiento no descansa en el RLS (decorativo en este
/// despliegue).</para>
/// </summary>
internal sealed class MaestroRadicadoLookup(FlitDbContext db) : IMaestroRadicadoLookup
{
    public Task<Guid?> AttachmentRadicadoAsync(Guid tenantId, Guid instanceId, CancellationToken ct = default) =>
        DelTramite(tenantId, instanceId)
            .Where(q => q.RegisteredAt != null
                && (q.Status == QuipuxSubmissionEstado.Registrado || q.Status == QuipuxSubmissionEstado.Aprobado))
            .OrderByDescending(q => q.RegisteredAt)
            .Select(q => (Guid?)q.AttachmentId)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlySet<Guid>> AttachmentsProtegidosAsync(
        Guid tenantId, Guid instanceId, CancellationToken ct = default)
    {
        var ids = await DelTramite(tenantId, instanceId)
            .Where(q => q.Status != QuipuxSubmissionEstado.Fallido)
            .Select(q => q.AttachmentId)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return ids.ToHashSet();
    }

    private IQueryable<QuipuxSubmission> DelTramite(Guid tenantId, Guid instanceId) =>
        db.QuipuxSubmissions
            .AsNoTracking()
            .Where(q => q.TenantId == tenantId && q.ProcedureInstanceId == instanceId);
}
