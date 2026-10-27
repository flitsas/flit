using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #13664 — <see cref="IProcedureInstanceFamilyLookup"/> sobre <c>tramites.procedure_instances</c> ⨝ <c>procedure_types</c>:
/// proyecta ÚNICAMENTE el código de familia del tipo, filtrado por id y por los tenants del alcance de la petición.
/// </summary>
internal sealed class ProcedureInstanceFamilyLookup(FlitDbContext db) : IProcedureInstanceFamilyLookup
{
    public async Task<ProcedureFamily?> GetFamilyAsync(
        Guid procedureInstanceId, IReadOnlyCollection<Guid>? tenantIds, CancellationToken ct = default)
    {
        var instances = db.ProcedureInstances.AsNoTracking().Where(x => x.Id == procedureInstanceId);
        if (tenantIds is not null)
        {
            var ids = tenantIds.ToList();
            instances = instances.Where(x => ids.Contains(x.TenantId));
        }

        var code = await instances
            .Join(db.ProcedureTypes.AsNoTracking(), i => i.ProcedureTypeId, t => t.Id, (_, t) => t.Family)
            .Cast<string?>()
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return code is null ? null : ProcedureFamilyCodes.FromCodeOrOtros(code);
    }

    public async Task<IReadOnlyDictionary<Guid, ProcedureFamily>> GetFamiliesAsync(
        IReadOnlyCollection<Guid> procedureInstanceIds, IReadOnlyCollection<Guid>? tenantIds, CancellationToken ct = default)
    {
        if (procedureInstanceIds.Count == 0)
            return new Dictionary<Guid, ProcedureFamily>();

        var wanted = procedureInstanceIds.Distinct().ToList();
        var instances = db.ProcedureInstances.AsNoTracking().Where(x => wanted.Contains(x.Id));
        if (tenantIds is not null)
        {
            var ids = tenantIds.ToList();
            instances = instances.Where(x => ids.Contains(x.TenantId));
        }

        var rows = await instances
            .Join(db.ProcedureTypes.AsNoTracking(), i => i.ProcedureTypeId, t => t.Id, (i, t) => new { i.Id, t.Family })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows.ToDictionary(r => r.Id, r => ProcedureFamilyCodes.FromCodeOrOtros(r.Family));
    }
}
