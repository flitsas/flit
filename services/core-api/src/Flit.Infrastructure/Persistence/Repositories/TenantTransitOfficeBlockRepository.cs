using System.Text.Json;
using Flit.Admin.Application.Auditing;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core de bloqueos de OT para cabezas Marca Blanca (HU #12407).
/// </summary>
internal sealed class TenantTransitOfficeBlockRepository : ITenantTransitOfficeBlockRepository
{
    private const string EntityName = "tenant_transit_office_blocks";
    private const string FieldName = "transit_office_ids";

    private readonly FlitDbContext _context;
    private readonly IAuditContextAccessor _auditContext;

    public TenantTransitOfficeBlockRepository(FlitDbContext context, IAuditContextAccessor auditContext)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _auditContext = auditContext ?? throw new ArgumentNullException(nameof(auditContext));
    }

    public async Task<IReadOnlyList<Guid>> ListBlockedOfficeIdsAsync(
        Guid headTenantId,
        CancellationToken cancellationToken = default) =>
        await _context.TenantTransitOfficeBlocks
            .AsNoTracking()
            .Where(b => b.TenantId == headTenantId)
            .OrderBy(b => b.CreatedAt)
            .Select(b => b.TransitOfficeId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<bool> AddBlockAsync(
        Guid headTenantId,
        Guid transitOfficeId,
        Guid? createdBy,
        Guid? correlationId,
        CancellationToken cancellationToken = default) =>
        ExecuteInTenantScopeAsync(
            headTenantId,
            () => PersistAddAsync(headTenantId, transitOfficeId, createdBy, correlationId, cancellationToken),
            cancellationToken);

    public Task<bool> RemoveBlockAsync(
        Guid headTenantId,
        Guid transitOfficeId,
        Guid? changedBy,
        Guid? correlationId,
        CancellationToken cancellationToken = default) =>
        ExecuteInTenantScopeAsync(
            headTenantId,
            () => PersistRemoveAsync(headTenantId, transitOfficeId, changedBy, correlationId, cancellationToken),
            cancellationToken);

    private async Task<bool> ExecuteInTenantScopeAsync(
        Guid tenantId,
        Func<Task<bool>> persist,
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

                    var result = await persist().ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return result;
                }
            }).ConfigureAwait(false);
        }

        return await persist().ConfigureAwait(false);
    }

    private async Task<bool> PersistAddAsync(
        Guid headTenantId,
        Guid transitOfficeId,
        Guid? createdBy,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var alreadyExists = await _context.TenantTransitOfficeBlocks
            .AnyAsync(b => b.TenantId == headTenantId && b.TransitOfficeId == transitOfficeId, cancellationToken)
            .ConfigureAwait(false);

        if (alreadyExists)
        {
            return false;
        }

        var current = await ListBlockedOfficeIdsAsync(headTenantId, cancellationToken).ConfigureAwait(false);
        var oldValue = JsonSerializer.Serialize(current);
        var now = DateTimeOffset.UtcNow;

        _context.TenantTransitOfficeBlocks.Add(new TenantTransitOfficeBlock
        {
            Id = Guid.NewGuid(),
            TenantId = headTenantId,
            TransitOfficeId = transitOfficeId,
            CreatedAt = now,
            CreatedBy = createdBy,
        });

        var newList = current.Concat([transitOfficeId]).OrderBy(id => id).ToList();
        var newValue = JsonSerializer.Serialize(newList);

        _context.TenantConfigAuditLogs.Add(new TenantConfigAuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = headTenantId,
            EntityName = EntityName,
            FieldName = FieldName,
            OldValue = oldValue,
            NewValue = newValue,
            ChangedAt = now,
            ChangedBy = createdBy,
            CorrelationId = correlationId,
            ClientIp = _auditContext.ClientIp,
            Operation = AuditVocabulary.Operations.Create,
            Result = AuditVocabulary.Results.Success,
        });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> PersistRemoveAsync(
        Guid headTenantId,
        Guid transitOfficeId,
        Guid? changedBy,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var block = await _context.TenantTransitOfficeBlocks
            .FirstOrDefaultAsync(
                b => b.TenantId == headTenantId && b.TransitOfficeId == transitOfficeId,
                cancellationToken)
            .ConfigureAwait(false);

        if (block is null)
        {
            return false;
        }

        var current = await ListBlockedOfficeIdsAsync(headTenantId, cancellationToken).ConfigureAwait(false);
        var oldValue = JsonSerializer.Serialize(current);
        var now = DateTimeOffset.UtcNow;

        _context.TenantTransitOfficeBlocks.Remove(block);

        var newList = current.Where(id => id != transitOfficeId).ToList();
        var newValue = JsonSerializer.Serialize(newList);

        _context.TenantConfigAuditLogs.Add(new TenantConfigAuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = headTenantId,
            EntityName = EntityName,
            FieldName = FieldName,
            OldValue = oldValue,
            NewValue = newValue,
            ChangedAt = now,
            ChangedBy = changedBy,
            CorrelationId = correlationId,
            ClientIp = _auditContext.ClientIp,
            Operation = AuditVocabulary.Operations.Delete,
            Result = AuditVocabulary.Results.Success,
        });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
