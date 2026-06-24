using Flit.Admin.Domain.OtDocumentPrecedence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>Prelación documental OT (HU #10222).</summary>
internal sealed class OtDocumentPrecedenceRepository : IOtDocumentPrecedenceRepository
{
    private const int MaxBatchSize = 50;

    private readonly FlitDbContext _context;

    public OtDocumentPrecedenceRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<OtDocumentPrecedenceItem>> ListByProcedureTypeAsync(
        Guid tenantId,
        Guid procedureTypeId,
        CancellationToken cancellationToken = default) =>
        await ExecuteInTenantScopeAsync(
            tenantId,
            async () =>
            {
                var query =
                    from p in _context.OtDocumentPrecedences.AsNoTracking()
                    join d in _context.DocumentTypes.AsNoTracking() on p.DocumentTypeId equals d.Id
                    where p.TenantId == tenantId && p.ProcedureTypeId == procedureTypeId
                    orderby p.SortOrder
                    select new OtDocumentPrecedenceItem
                    {
                        Id = p.Id,
                        TenantId = p.TenantId,
                        ProcedureTypeId = p.ProcedureTypeId,
                        DocumentTypeId = p.DocumentTypeId,
                        DocumentName = d.Name,
                        SortOrder = p.SortOrder,
                    };

                return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<OtDocumentPrecedenceItem>?> ReorderBatchAsync(
        Guid tenantId,
        Guid procedureTypeId,
        IReadOnlyList<OtDocumentPrecedenceOrderItem> items,
        Guid? changedBy,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0 || items.Count > MaxBatchSize)
        {
            return null;
        }

        return await ExecuteInTenantScopeAsync(
            tenantId,
            async () =>
            {
                var existing = await _context.OtDocumentPrecedences
                    .Where(p => p.TenantId == tenantId && p.ProcedureTypeId == procedureTypeId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (existing.Count == 0)
                {
                    return null;
                }

                var lookup = existing.ToDictionary(e => e.DocumentTypeId);
                foreach (var item in items)
                {
                    if (!lookup.ContainsKey(item.DocumentTypeId))
                    {
                        return null;
                    }
                }

                foreach (var item in items)
                {
                    lookup[item.DocumentTypeId].SortOrder = item.SortOrder;
                }

                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return await ListByProcedureTypeAsync(tenantId, procedureTypeId, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> ExecuteInTenantScopeAsync<T>(
        Guid tenantId,
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (_context.Database.IsRelational())
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                var transaction = await _context.Database
                    .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                await using (transaction.ConfigureAwait(false))
                {
                    await _context.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)",
                        cancellationToken).ConfigureAwait(false);

                    var result = await action().ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return result;
                }
            }).ConfigureAwait(false);
        }

        return await action().ConfigureAwait(false);
    }
}
