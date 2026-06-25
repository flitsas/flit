using System.Text.Json;
using Flit.Admin.Domain.Common;
using Flit.Admin.Domain.OtClientProcedures;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Trámites de clientes OT — lectura/escritura cross-tenant vía grants (HU #10217).
/// En PostgreSQL desactiva RLS localmente solo para joins autorizados por grant;
/// en InMemory filtra explícitamente por tenant y transit_office_id.
/// </summary>
internal sealed class OtClientProcedureRepository : IOtClientProcedureRepository
{
    private readonly FlitDbContext _context;

    public OtClientProcedureRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<PagedResult<OtClientProcedure>> ListAsync(
        Guid otTenantId,
        OtClientProcedureFilter filter,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default) =>
        ExecuteOtScopedAsync(
            otTenantId,
            transitOfficeIdOverride,
            async transitOfficeId =>
            {
                var clientTenantIds = await ListGrantedClientTenantIdsAsync(
                    transitOfficeId,
                    cancellationToken).ConfigureAwait(false);

                if (clientTenantIds.Count == 0)
                {
                    return PagedResult<OtClientProcedure>.Empty;
                }

                return await ExecuteCrossTenantReadAsync(
                    async () =>
                    {
                        var query = BuildAccessibleQuery(transitOfficeId, clientTenantIds);

                        if (!string.IsNullOrWhiteSpace(filter.Status))
                        {
                            query = query.Where(p => p.Status == filter.Status.Trim());
                        }

                        if (filter.ProcedureTypeId is not null)
                        {
                            query = query.Where(p => p.ProcedureTypeId == filter.ProcedureTypeId.Value);
                        }

                        var totalCount = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
                        if (totalCount == 0)
                        {
                            return PagedResult<OtClientProcedure>.Empty;
                        }

                        var items = await query
                            .OrderByDescending(p => p.CreatedAt)
                            .ThenByDescending(p => p.Id)
                            .Skip((filter.Page - 1) * filter.PageSize)
                            .Take(filter.PageSize)
                            .Select(p => new OtClientProcedure
                            {
                                Id = p.Id,
                                ClientTenantId = p.TenantId,
                                ProcedureTypeId = p.ProcedureTypeId,
                                ReferenceNumber = p.ReferenceNumber,
                                Status = p.Status,
                                TransitOfficeId = p.TransitOfficeId,
                                CreatedAt = p.CreatedAt,
                                SubmittedAt = p.SubmittedAt,
                            })
                            .ToListAsync(cancellationToken)
                            .ConfigureAwait(false);

                        var enriched = await EnrichDisplayNamesAsync(items, cancellationToken)
                            .ConfigureAwait(false);

                        return new PagedResult<OtClientProcedure>(enriched, totalCount);
                    },
                    cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

    public Task<OtClientProcedure?> GetByIdAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        CancellationToken cancellationToken = default) =>
        ExecuteOtScopedAsync(
            otTenantId,
            transitOfficeId => FindAccessibleProcedureAsync(
                transitOfficeId,
                procedureInstanceId,
                cancellationToken),
            cancellationToken);

    public Task<OtClientProcedure?> ApproveAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        Guid? approvedBy,
        CancellationToken cancellationToken = default) =>
        TransitionAsync(
            otTenantId,
            procedureInstanceId,
            ProcedureInstanceStatus.PendingOt,
            ProcedureInstanceStatus.ApprovedOt,
            approvedBy,
            reason: null,
            cancellationToken);

    public Task<OtClientProcedure?> RejectAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        string reason,
        Guid? rejectedBy,
        CancellationToken cancellationToken = default) =>
        TransitionAsync(
            otTenantId,
            procedureInstanceId,
            ProcedureInstanceStatus.PendingOt,
            ProcedureInstanceStatus.RejectedOt,
            rejectedBy,
            reason,
            cancellationToken);

    private async Task<OtClientProcedure?> TransitionAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        string expectedStatus,
        string targetStatus,
        Guid? changedBy,
        string? reason,
        CancellationToken cancellationToken)
    {
        var accessible = await ExecuteOtScopedAsync(
            otTenantId,
            transitOfficeId => FindAccessibleProcedureAsync(
                transitOfficeId,
                procedureInstanceId,
                cancellationToken),
            cancellationToken).ConfigureAwait(false);

        if (accessible is null)
        {
            return null;
        }

        return await ExecuteInClientTenantScopeAsync(
            accessible.ClientTenantId,
            async () =>
            {
                var entity = await _context.ProcedureInstances
                    .FirstOrDefaultAsync(
                        p => p.Id == procedureInstanceId
                            && p.TenantId == accessible.ClientTenantId
                            && p.DeletedAt == null,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (entity is null || entity.Status != expectedStatus)
                {
                    return null;
                }

                var resolvedChangedBy = await ResolveChangedByAsync(changedBy, cancellationToken)
                    .ConfigureAwait(false);
                var now = DateTimeOffset.UtcNow;
                entity.Status = targetStatus;
                entity.UpdatedAt = now;
                entity.UpdatedBy = resolvedChangedBy;

                _context.ProcedureInstanceStatusHistories.Add(new ProcedureInstanceStatusHistory
                {
                    Id = Guid.NewGuid(),
                    TenantId = accessible.ClientTenantId,
                    ProcedureInstanceId = entity.Id,
                    FromStatus = expectedStatus,
                    ToStatus = targetStatus,
                    ChangedAt = now,
                    ChangedBy = resolvedChangedBy,
                    Reason = reason,
                    Metadata = JsonSerializer.Serialize(new
                    {
                        ot_tenant_id = otTenantId,
                        approver_tenant_id = otTenantId,
                    }),
                });

                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                var mapped = Map(entity);
                var enriched = await EnrichDisplayNamesAsync([mapped], cancellationToken)
                    .ConfigureAwait(false);
                return enriched[0];
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<OtClientProcedure?> FindAccessibleProcedureAsync(
        Guid transitOfficeId,
        Guid procedureInstanceId,
        CancellationToken cancellationToken)
    {
        var clientTenantIds = await ListGrantedClientTenantIdsAsync(
            transitOfficeId,
            cancellationToken).ConfigureAwait(false);

        if (clientTenantIds.Count == 0)
        {
            return null;
        }

        return await ExecuteCrossTenantReadAsync(
            async () =>
            {
                var mapped = await BuildAccessibleQuery(transitOfficeId, clientTenantIds)
                    .Where(p => p.Id == procedureInstanceId)
                    .Select(p => new OtClientProcedure
                    {
                        Id = p.Id,
                        ClientTenantId = p.TenantId,
                        ProcedureTypeId = p.ProcedureTypeId,
                        ReferenceNumber = p.ReferenceNumber,
                        Status = p.Status,
                        TransitOfficeId = p.TransitOfficeId,
                        CreatedAt = p.CreatedAt,
                        SubmittedAt = p.SubmittedAt,
                    })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (mapped is null)
                {
                    return null;
                }

                var enriched = await EnrichDisplayNamesAsync([mapped], cancellationToken)
                    .ConfigureAwait(false);
                return enriched[0];
            },
            cancellationToken).ConfigureAwait(false);
    }

    private IQueryable<ProcedureInstance> BuildAccessibleQuery(
        Guid transitOfficeId,
        IReadOnlyList<Guid> clientTenantIds) =>
        _context.ProcedureInstances
            .AsNoTracking()
            .Where(p => p.DeletedAt == null
                && p.TransitOfficeId == transitOfficeId
                && clientTenantIds.Contains(p.TenantId));

    private async Task<Guid?> ResolveTransitOfficeIdAsync(
        Guid otTenantId,
        CancellationToken cancellationToken)
    {
        var profile = await ExecuteInOtTenantScopeAsync(
            otTenantId,
            async () => await _context.TransitOfficeProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.TenantId == otTenantId, cancellationToken)
                .ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        return profile?.TransitOfficeId;
    }

    private async Task<IReadOnlyList<Guid>> ListGrantedClientTenantIdsAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken) =>
        await ExecuteCrossTenantReadAsync(
            async () => (IReadOnlyList<Guid>)await _context.TenantTransitOfficeGrants
                .AsNoTracking()
                .Where(g => g.TransitOfficeId == transitOfficeId && g.IsEnabled)
                .Select(g => g.TenantId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

    private async Task<T> ExecuteOtScopedAsync<T>(
        Guid otTenantId,
        Guid? transitOfficeIdOverride,
        Func<Guid, Task<T>> action,
        CancellationToken cancellationToken)
    {
        Guid? transitOfficeId = transitOfficeIdOverride is Guid overrideId && overrideId != Guid.Empty
            ? overrideId
            : await ResolveTransitOfficeIdAsync(otTenantId, cancellationToken).ConfigureAwait(false);

        if (transitOfficeId is null)
        {
            return typeof(T) == typeof(PagedResult<OtClientProcedure>)
                ? (T)(object)PagedResult<OtClientProcedure>.Empty
                : default!;
        }

        return await action(transitOfficeId.Value).ConfigureAwait(false);
    }

    private async Task<T> ExecuteOtScopedAsync<T>(
        Guid otTenantId,
        Func<Guid, Task<T>> action,
        CancellationToken cancellationToken) =>
        await ExecuteOtScopedAsync(otTenantId, null, action, cancellationToken).ConfigureAwait(false);

    private async Task<T> ExecuteCrossTenantReadAsync<T>(
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
                    await _context.Database.ExecuteSqlRawAsync(
                        "SET LOCAL row_security = off",
                        cancellationToken).ConfigureAwait(false);

                    var result = await action().ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return result;
                }
            }).ConfigureAwait(false);
        }

        return await action().ConfigureAwait(false);
    }

    private async Task<T> ExecuteInOtTenantScopeAsync<T>(
        Guid otTenantId,
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
                        $"SELECT set_config('app.current_tenant_id', {otTenantId.ToString()}, true)",
                        cancellationToken).ConfigureAwait(false);

                    var result = await action().ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return result;
                }
            }).ConfigureAwait(false);
        }

        return await action().ConfigureAwait(false);
    }

    private async Task<T> ExecuteInClientTenantScopeAsync<T>(
        Guid clientTenantId,
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
                        $"SELECT set_config('app.current_tenant_id', {clientTenantId.ToString()}, true)",
                        cancellationToken).ConfigureAwait(false);

                    var result = await action().ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return result;
                }
            }).ConfigureAwait(false);
        }

        return await action().ConfigureAwait(false);
    }

    /// <summary>Evita violación FK si el JWT sub no existe en identity.users.</summary>
    private async Task<Guid?> ResolveChangedByAsync(Guid? changedBy, CancellationToken cancellationToken)
    {
        if (changedBy is null || changedBy == Guid.Empty)
        {
            return null;
        }

        var exists = await _context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == changedBy.Value, cancellationToken)
            .ConfigureAwait(false);

        return exists ? changedBy : null;
    }

    private static OtClientProcedure Map(ProcedureInstance entity) => new()
    {
        Id = entity.Id,
        ClientTenantId = entity.TenantId,
        ProcedureTypeId = entity.ProcedureTypeId,
        ReferenceNumber = entity.ReferenceNumber,
        Status = entity.Status,
        TransitOfficeId = entity.TransitOfficeId,
        CreatedAt = entity.CreatedAt,
        SubmittedAt = entity.SubmittedAt,
    };

    private async Task<IReadOnlyList<OtClientProcedure>> EnrichDisplayNamesAsync(
        List<OtClientProcedure> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return items;
        }

        var typeIds = items.Select(i => i.ProcedureTypeId).Distinct().ToList();
        var tenantIds = items.Select(i => i.ClientTenantId).Distinct().ToList();

        var typeNames = await _context.ProcedureTypes
            .AsNoTracking()
            .Where(pt => typeIds.Contains(pt.Id))
            .ToDictionaryAsync(pt => pt.Id, pt => pt.Name, cancellationToken)
            .ConfigureAwait(false);

        var tenantNames = await _context.Tenants
            .AsNoTracking()
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.LegalName, cancellationToken)
            .ConfigureAwait(false);

        return items
            .Select(item => new OtClientProcedure
            {
                Id = item.Id,
                ClientTenantId = item.ClientTenantId,
                ClientTenantName = tenantNames.GetValueOrDefault(item.ClientTenantId, "—"),
                ProcedureTypeId = item.ProcedureTypeId,
                ProcedureTypeName = typeNames.GetValueOrDefault(item.ProcedureTypeId, "—"),
                ReferenceNumber = item.ReferenceNumber,
                Status = item.Status,
                TransitOfficeId = item.TransitOfficeId,
                CreatedAt = item.CreatedAt,
                SubmittedAt = item.SubmittedAt,
            })
            .ToList();
    }
}
