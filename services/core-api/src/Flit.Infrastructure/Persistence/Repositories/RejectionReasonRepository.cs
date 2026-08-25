using Flit.Admin.Domain.RejectionReasons;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Catálogo global de causales de rechazo. Sin scope de tenant ni RLS: la tabla no tiene
/// <c>tenant_id</c> (es catálogo compartido, mismo criterio que <c>catalogs.transit_offices</c>).
/// </summary>
internal sealed class RejectionReasonRepository : IRejectionReasonRepository
{
    private readonly FlitDbContext _context;

    public RejectionReasonRepository(FlitDbContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<IReadOnlyList<RejectionReasonItem>> ListAsync(
        string? familia,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var query = _context.RejectionReasons.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(familia))
        {
            query = query.Where(r => r.Family == familia);
        }

        if (!includeInactive)
        {
            query = query.Where(r => r.IsActive);
        }

        return await query
            .OrderBy(r => r.Family)
            .ThenBy(r => r.SortOrder)
            .ThenBy(r => r.Description)
            .Select(r => new RejectionReasonItem(
                r.Id, r.Code, r.Description, r.Family, r.SortOrder, r.IsActive))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<RejectionReasonItem?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        await _context.RejectionReasons
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new RejectionReasonItem(
                r.Id, r.Code, r.Description, r.Family, r.SortOrder, r.IsActive))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<bool> CodeExistsAsync(
        string code,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default) =>
        await _context.RejectionReasons
            .AsNoTracking()
            .AnyAsync(
                r => r.Code == code && (excludeId == null || r.Id != excludeId),
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<RejectionReasonItem> CreateAsync(
        string code,
        string description,
        string familia,
        int sortOrder,
        Guid? createdBy,
        CancellationToken cancellationToken = default)
    {
        var entity = new RejectionReason
        {
            Id = Guid.NewGuid(),
            Code = code,
            Description = description,
            Family = familia,
            SortOrder = sortOrder,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = createdBy,
        };

        _context.RejectionReasons.Add(entity);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Map(entity);
    }

    public async Task<RejectionReasonItem?> UpdateAsync(
        Guid id,
        string code,
        string description,
        string familia,
        int sortOrder,
        Guid? updatedBy,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.RejectionReasons
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return null;
        }

        entity.Code = code;
        entity.Description = description;
        entity.Family = familia;
        entity.SortOrder = sortOrder;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = updatedBy;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Map(entity);
    }

    public async Task<RejectionReasonItem?> SetActiveAsync(
        Guid id,
        bool isActive,
        Guid? updatedBy,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.RejectionReasons
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return null;
        }

        entity.IsActive = isActive;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = updatedBy;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Map(entity);
    }

    public async Task<IReadOnlyList<Guid>> FilterValidIdsAsync(
        IReadOnlyList<Guid> candidateIds,
        string familia,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidateIds);

        if (candidateIds.Count == 0)
        {
            return [];
        }

        // Distinct en el filtro y no en la consulta: si el cliente manda la misma causal dos veces
        // no debe contar doble en el reporte.
        var unique = candidateIds.Distinct().ToList();

        return await _context.RejectionReasons
            .AsNoTracking()
            .Where(r => unique.Contains(r.Id) && r.IsActive && r.Family == familia)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static RejectionReasonItem Map(RejectionReason entity) =>
        new(entity.Id, entity.Code, entity.Description, entity.Family, entity.SortOrder, entity.IsActive);
}
