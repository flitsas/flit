using Flit.Admin.Domain.DocumentOrderOverrides;
using Flit.Infrastructure.Persistence.Entities.Tramites;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core de los overrides de orden documental (HU #10196).
///
/// <c>tramites.document_order_overrides</c> es parte del catálogo global SuperAdmin
/// (sin RLS). Todas las consultas usan EF LINQ parametrizado y enriquecen el read model
/// con el documento (join a <c>document_types</c>). El borrado (AC6) es físico.
/// </summary>
internal sealed class DocumentOrderOverrideRepository : IDocumentOrderOverrideRepository
{
    private readonly FlitDbContext _context;

    public DocumentOrderOverrideRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<DocumentOrderOverrideItem> CreateAsync(
        Guid procedureTypeId,
        Guid documentTypeId,
        string scopeType,
        Guid scopeRefId,
        short orden,
        Guid? createdBy,
        CancellationToken cancellationToken = default)
    {
        var entity = new DocumentOrderOverride
        {
            Id = Guid.NewGuid(),
            ProcedureTypeId = procedureTypeId,
            DocumentTypeId = documentTypeId,
            ScopeType = scopeType,
            ScopeRefId = scopeRefId,
            SortOrder = orden,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = createdBy,
        };

        _context.DocumentOrderOverrides.Add(entity);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await QueryById(entity.Id).FirstAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DocumentOrderOverrideItem>> ListByScopeAsync(
        Guid procedureTypeId,
        string scopeType,
        Guid scopeRefId,
        CancellationToken cancellationToken = default)
    {
        var items = await Enrich(
                _context.DocumentOrderOverrides
                    .AsNoTracking()
                    .Where(o => o.ProcedureTypeId == procedureTypeId
                        && o.ScopeType == scopeType
                        && o.ScopeRefId == scopeRefId))
            .OrderBy(o => o.Orden)
            .ThenBy(o => o.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return items;
    }

    public Task<DocumentOrderOverrideItem?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        QueryById(id).FirstOrDefaultAsync(cancellationToken);

    public async Task<DocumentOrderOverrideItem?> UpdateOrderAsync(
        Guid id,
        short orden,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.DocumentOrderOverrides
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return null;
        }

        entity.SortOrder = orden;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await QueryById(entity.Id).FirstAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.DocumentOrderOverrides
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return false;
        }

        _context.DocumentOrderOverrides.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    public Task<bool> ExistsUniqueAsync(
        Guid procedureTypeId,
        Guid documentTypeId,
        string scopeType,
        Guid scopeRefId,
        CancellationToken cancellationToken = default) =>
        _context.DocumentOrderOverrides
            .AsNoTracking()
            .AnyAsync(
                o => o.ProcedureTypeId == procedureTypeId
                    && o.DocumentTypeId == documentTypeId
                    && o.ScopeType == scopeType
                    && o.ScopeRefId == scopeRefId,
                cancellationToken);

    private IQueryable<DocumentOrderOverrideItem> QueryById(Guid id) =>
        Enrich(_context.DocumentOrderOverrides.AsNoTracking().Where(o => o.Id == id));

    /// <summary>
    /// Proyecta el override enriquecido con los datos del documento (join a
    /// <c>document_types</c>) sobre el read model del dominio.
    /// </summary>
    private IQueryable<DocumentOrderOverrideItem> Enrich(
        IQueryable<DocumentOrderOverride> source) =>
        from o in source
        join d in _context.DocumentTypes.AsNoTracking() on o.DocumentTypeId equals d.Id
        select new DocumentOrderOverrideItem
        {
            Id = o.Id,
            ProcedureTypeId = o.ProcedureTypeId,
            DocumentTypeId = o.DocumentTypeId,
            ScopeType = o.ScopeType,
            ScopeRefId = o.ScopeRefId!.Value,
            Orden = o.SortOrder,
            DocumentCode = d.Code,
            DocumentName = d.Name,
        };
}
