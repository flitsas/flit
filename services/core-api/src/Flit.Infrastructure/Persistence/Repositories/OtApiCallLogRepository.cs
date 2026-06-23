using Flit.Admin.Domain.Common;
using Flit.Admin.Domain.OtWebhooks;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>Repositorio de bitácora API OT (HU #10216).</summary>
internal sealed class OtApiCallLogRepository : IOtApiCallLogRepository
{
    private readonly FlitDbContext _context;

    public OtApiCallLogRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<PagedResult<OtApiCallLog>> ListPagedAsync(
        Guid tenantId,
        OtApiCallLogFilter filter,
        CancellationToken cancellationToken = default) =>
        ExecuteInTenantScopeAsync(
            tenantId,
            async () =>
            {
                var query = _context.OtApiCallLogs
                    .AsNoTracking()
                    .Where(l => l.TenantId == tenantId);

                if (!string.IsNullOrWhiteSpace(filter.Direction))
                {
                    query = query.Where(l => l.Direction == filter.Direction);
                }

                if (filter.From is not null)
                {
                    query = query.Where(l => l.CalledAt >= filter.From.Value);
                }

                if (filter.To is not null)
                {
                    query = query.Where(l => l.CalledAt <= filter.To.Value);
                }

                var totalCount = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
                if (totalCount == 0)
                {
                    return PagedResult<OtApiCallLog>.Empty;
                }

                var items = await query
                    .OrderByDescending(l => l.CalledAt)
                    .ThenByDescending(l => l.Id)
                    .Skip((filter.Page - 1) * filter.PageSize)
                    .Take(filter.PageSize)
                    .Select(l => Map(l))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                return new PagedResult<OtApiCallLog>(items, totalCount);
            },
            cancellationToken);

    public Task<OtApiCallLog> AppendAsync(
        Guid tenantId,
        string direction,
        string endpoint,
        string httpMethod,
        string payloadHash,
        short? responseCode,
        int? durationMs,
        Guid? correlationId,
        CancellationToken cancellationToken = default) =>
        ExecuteInTenantScopeAsync(
            tenantId,
            async () =>
            {
                var entity = new OtApiCallLogEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Direction = direction,
                    Endpoint = endpoint,
                    HttpMethod = httpMethod,
                    PayloadHash = payloadHash,
                    ResponseCode = responseCode,
                    DurationMs = durationMs,
                    CalledAt = DateTimeOffset.UtcNow,
                    CorrelationId = correlationId,
                };

                _context.OtApiCallLogs.Add(entity);
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return Map(entity);
            },
            cancellationToken);

    private static OtApiCallLog Map(OtApiCallLogEntity entity) => new()
    {
        Id = entity.Id,
        TenantId = entity.TenantId,
        Direction = entity.Direction,
        Endpoint = entity.Endpoint,
        HttpMethod = entity.HttpMethod,
        PayloadHash = entity.PayloadHash,
        ResponseCode = entity.ResponseCode,
        DurationMs = entity.DurationMs,
        CalledAt = entity.CalledAt,
        CorrelationId = entity.CorrelationId,
    };

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
