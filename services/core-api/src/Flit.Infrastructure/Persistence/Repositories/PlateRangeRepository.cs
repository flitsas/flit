using Flit.Admin.Domain.PlatePreassign;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Inventario de rangos de placas (HU #10650, Feature #10587). RLS permisiva a nivel BD; el scope
/// de tenant se fija por consistencia de auditoría. Patrón de <see cref="OtRequirementsRepository"/>.
/// </summary>
internal sealed class PlateRangeRepository : IPlateRangeRepository
{
    private readonly FlitDbContext _context;

    public PlateRangeRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<CreatePlateRangeResult> CreateRangeAsync(
        Guid companyTenantId,
        Guid transitOfficeId,
        string prefix,
        int rangeFrom,
        int rangeTo,
        Guid? createdBy,
        CancellationToken cancellationToken = default) =>
        ExecuteInTenantScopeAsync(
            companyTenantId,
            async () =>
            {
                var normalizedPrefix = prefix?.Trim().ToUpperInvariant() ?? string.Empty;
                var error = PlateRangeRules.Validate(normalizedPrefix, rangeFrom, rangeTo);
                if (error is not null)
                {
                    return CreatePlateRangeResult.Fail(error);
                }

                var plates = PlateRangeRules.Enumerate(normalizedPrefix, rangeFrom, rangeTo).ToList();

                // Rechaza solapamiento con placas ya registradas para el mismo OT (UNIQUE office+plate).
                var existing = await _context.PlateRangeDetails
                    .AsNoTracking()
                    .Where(d => d.TransitOfficeId == transitOfficeId && plates.Contains(d.Plate))
                    .Select(d => d.Plate)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (existing is not null)
                {
                    return CreatePlateRangeResult.Fail(
                        $"El rango se solapa con placas ya registradas para el OT (ej. {existing}).");
                }

                var now = DateTimeOffset.UtcNow;
                var range = new PlateRangeEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = companyTenantId,
                    TransitOfficeId = transitOfficeId,
                    Prefix = normalizedPrefix,
                    RangeFrom = rangeFrom,
                    RangeTo = rangeTo,
                    EditableUntil = now.Add(PlateRangeRules.EditWindow),
                    CreatedAt = now,
                    CreatedBy = createdBy,
                };
                _context.PlateRanges.Add(range);

                foreach (var plate in plates)
                {
                    _context.PlateRangeDetails.Add(new PlateRangeDetailEntity
                    {
                        Id = Guid.NewGuid(),
                        PlateRangeId = range.Id,
                        TenantId = companyTenantId,
                        TransitOfficeId = transitOfficeId,
                        Plate = plate,
                        State = PlateState.Disponible,
                        CreatedAt = now,
                    });
                }

                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return CreatePlateRangeResult.Ok(range.Id, plates.Count);
            },
            cancellationToken);

    public Task<IReadOnlyList<PlateRangeSummary>> ListRangesAsync(
        Guid companyTenantId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default) =>
        ExecuteInTenantScopeAsync(
            companyTenantId,
            async () =>
            {
                var ranges = await _context.PlateRanges
                    .AsNoTracking()
                    .Where(r => r.TenantId == companyTenantId
                        && (transitOfficeId == null || r.TransitOfficeId == transitOfficeId))
                    .OrderByDescending(r => r.CreatedAt)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var result = new List<PlateRangeSummary>(ranges.Count);
                foreach (var r in ranges)
                {
                    var available = await _context.PlateRangeDetails
                        .AsNoTracking()
                        .CountAsync(
                            d => d.PlateRangeId == r.Id && d.State == PlateState.Disponible,
                            cancellationToken)
                        .ConfigureAwait(false);

                    result.Add(new PlateRangeSummary(
                        r.Id, r.TenantId, r.TransitOfficeId, r.Prefix, r.RangeFrom, r.RangeTo,
                        r.EditableUntil, r.RangeTo - r.RangeFrom + 1, available));
                }

                return (IReadOnlyList<PlateRangeSummary>)result;
            },
            cancellationToken);

    public Task<IReadOnlyList<PlateDetail>> ListDetailsAsync(
        Guid companyTenantId,
        Guid? transitOfficeId,
        string? state,
        CancellationToken cancellationToken = default) =>
        ExecuteInTenantScopeAsync(
            companyTenantId,
            async () =>
            {
                var details = await _context.PlateRangeDetails
                    .AsNoTracking()
                    .Where(d => d.TenantId == companyTenantId
                        && (transitOfficeId == null || d.TransitOfficeId == transitOfficeId)
                        && (state == null || d.State == state))
                    .OrderBy(d => d.Plate)
                    .Select(d => new PlateDetail(
                        d.Id, d.PlateRangeId, d.TenantId, d.TransitOfficeId, d.Plate, d.State, d.ProcedureInstanceId))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                return (IReadOnlyList<PlateDetail>)details;
            },
            cancellationToken);

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
