using Flit.Tramites.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #12358 — <see cref="IProcedureInstanceOwnerLookup"/> sobre <c>tramites.procedure_instances</c>.
/// Proyecta ÚNICAMENTE <c>tenant_id</c> por id, sin filtro de tenant a propósito: el guard de escritura
/// necesita saber de quién es el trámite para responder 403 por <c>CanWrite</c> (AC4) en vez de
/// ocultarlo como 404. Incluye los borrados lógicos: escribir sobre un trámite ajeno borrado sigue
/// siendo una escritura ajena.
/// </summary>
internal sealed class ProcedureInstanceOwnerLookup(FlitDbContext db) : IProcedureInstanceOwnerLookup
{
    public async Task<Guid?> GetOwnerTenantIdAsync(Guid procedureInstanceId, CancellationToken ct = default)
    {
        var owner = await db.ProcedureInstances.AsNoTracking()
            .Where(x => x.Id == procedureInstanceId)
            .Select(x => (Guid?)x.TenantId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return owner;
    }

    public async Task<IReadOnlyDictionary<Guid, Guid>> GetOwnerTenantIdsAsync(
        IReadOnlyCollection<Guid> procedureInstanceIds, CancellationToken ct = default)
    {
        if (procedureInstanceIds.Count == 0)
            return new Dictionary<Guid, Guid>();

        var ids = procedureInstanceIds.Distinct().ToList();
        return await db.ProcedureInstances.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.TenantId })
            .ToDictionaryAsync(x => x.Id, x => x.TenantId, ct)
            .ConfigureAwait(false);
    }
}
