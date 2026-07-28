using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using Flit.Ict.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Ict.Infrastructure.Logging;

/// <summary>Escritura y lectura de <c>ict.integration_log</c>. Enmascara PII residual al servir (barrera 2).</summary>
public sealed class IntegrationLogRepository(IctDbContext db) : IIntegrationLogWriter, IIntegrationLogQuery
{
    public async Task WriteAsync(IntegrationLog log, CancellationToken ct = default)
    {
        db.IntegrationLogs.Add(log);
        await db.SaveChangesAsync(ct);
    }

    public async Task<LogPage> QueryAsync(LogQueryFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = db.IntegrationLogs.AsNoTracking().AsQueryable();

        if (filter.TenantId is { } tenantId)
        {
            query = query.Where(l => l.TenantId == tenantId);
        }

        if (!string.IsNullOrWhiteSpace(filter.LogType))
        {
            query = query.Where(l => l.LogType == filter.LogType);
        }

        if (filter.CorrelationId is { } correlationId)
        {
            query = query.Where(l => l.CorrelationId == correlationId);
        }

        if (filter.From is { } from)
        {
            query = query.Where(l => l.CreatedAt >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(l => l.CreatedAt <= to);
        }

        var total = await query.CountAsync(ct);
        var pageSize = Math.Clamp(filter.PageSize, 1, 200);
        var page = Math.Max(filter.Page, 1);

        var items = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        foreach (var item in items)
        {
            item.Headers = IctSensitiveDataMasker.MaskJson(item.Headers);
            item.Request = IctSensitiveDataMasker.MaskJson(item.Request);
            item.Response = IctSensitiveDataMasker.MaskJson(item.Response);
        }

        return new LogPage(items, total, page, pageSize);
    }
}
