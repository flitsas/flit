using Flit.Admin.Domain.Common;
using Flit.Admin.Domain.DocumentTypes;
using Flit.Infrastructure.Persistence.Entities.Tramites;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core del catálogo de tipos de documento (HU #10193).
///
/// <c>tramites.document_types</c> es un catálogo global SuperAdmin (sin RLS), por lo
/// que no requiere fijar <c>app.current_tenant_id</c>. Todas las consultas usan EF LINQ
/// parametrizado. El soft-delete (AC4) marca <c>is_active = false</c> sin borrado físico.
/// </summary>
internal sealed class DocumentTypeRepository : IDocumentTypeRepository
{
    private readonly FlitDbContext _context;

    public DocumentTypeRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<DocumentTypeListItem> CreateAsync(
        string code,
        string name,
        string? description,
        Guid? createdBy,
        CancellationToken cancellationToken = default)
    {
        var entity = new DocumentType
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            Description = description,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = createdBy,
        };

        _context.DocumentTypes.Add(entity);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Map(entity);
    }

    public async Task<PagedResult<DocumentTypeListItem>> ListAsync(
        DocumentTypeListFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = _context.DocumentTypes.AsNoTracking();

        if (!filter.IncludeInactive)
        {
            query = query.Where(d => d.IsActive);
        }

        var totalCount = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);

        if (totalCount == 0)
        {
            return PagedResult<DocumentTypeListItem>.Empty;
        }

        var items = await query
            .OrderBy(d => d.Name)
            .ThenBy(d => d.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(d => new DocumentTypeListItem
            {
                Id = d.Id,
                Code = d.Code,
                Name = d.Name,
                Description = d.Description,
                IsActive = d.IsActive,
                CreatedAt = d.CreatedAt,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<DocumentTypeListItem>(items, totalCount);
    }

    public async Task<DocumentTypeListItem?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.DocumentTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : Map(entity);
    }

    public async Task<DocumentTypeListItem?> UpdateAsync(
        Guid id,
        string code,
        string name,
        string? description,
        Guid? updatedBy,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.DocumentTypes
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return null;
        }

        entity.Code = code;
        entity.Name = name;
        entity.Description = description;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = updatedBy;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Map(entity);
    }

    public async Task<bool> SoftDeleteAsync(
        Guid id,
        Guid? updatedBy,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.DocumentTypes
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return false;
        }

        entity.IsActive = false;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = updatedBy;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    public async Task<bool> ReactivateAsync(
        Guid id,
        Guid? updatedBy,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.DocumentTypes
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return false;
        }

        entity.IsActive = true;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = updatedBy;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    public Task<bool> HasActiveAssociationsAsync(
        Guid documentTypeId,
        CancellationToken cancellationToken = default) =>
        _context.ProcedureDocumentRequirements
            .AsNoTracking()
            .AnyAsync(r => r.DocumentTypeId == documentTypeId, cancellationToken);

    public Task<bool> CodeExistsAsync(
        string code,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default) =>
        _context.DocumentTypes
            .AsNoTracking()
            .AnyAsync(d => d.Code == code && (excludeId == null || d.Id != excludeId), cancellationToken);

    private static DocumentTypeListItem Map(DocumentType entity) => new()
    {
        Id = entity.Id,
        Code = entity.Code,
        Name = entity.Name,
        Description = entity.Description,
        IsActive = entity.IsActive,
        CreatedAt = entity.CreatedAt,
    };
}
