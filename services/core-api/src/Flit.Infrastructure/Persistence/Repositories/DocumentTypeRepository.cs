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
        IReadOnlyList<string>? mimeTypesAllowed = null,
        long? maxSizeBytes = null,
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
            // RF08/09: null ⇒ vacío/0 ⇒ el AttachmentValidator cae a los límites globales.
            MimeTypesAllowed = mimeTypesAllowed?.ToList() ?? [],
            MaxSizeBytes = maxSizeBytes ?? 0,
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
                MimeTypesAllowed = d.MimeTypesAllowed,
                MaxSizeBytes = d.MaxSizeBytes,
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
        IReadOnlyList<string>? mimeTypesAllowed = null,
        long? maxSizeBytes = null,
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
        // RF08/09: solo se tocan los límites si el request los envía (null ⇒ conserva lo existente).
        if (mimeTypesAllowed is not null)
            entity.MimeTypesAllowed = mimeTypesAllowed.ToList();
        if (maxSizeBytes is not null)
            entity.MaxSizeBytes = maxSizeBytes.Value;

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

    public async Task<IReadOnlyList<DocumentTypeAssociationRef>> GetAssociatedProcedureTypesAsync(
        Guid documentTypeId,
        CancellationToken cancellationToken = default) =>
        // Subconsulta EXISTS sobre procedure_types: traduce a SQL en Postgres (a diferencia
        // de Join+Distinct sobre el DTO) y cada trámite aparece una sola vez de forma natural.
        await _context.ProcedureTypes
            .AsNoTracking()
            .Where(procedureType => _context.ProcedureDocumentRequirements
                .Any(r => r.DocumentTypeId == documentTypeId && r.ProcedureTypeId == procedureType.Id))
            .OrderBy(procedureType => procedureType.Name)
            .Select(procedureType => new DocumentTypeAssociationRef(procedureType.Code, procedureType.Name))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

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
        MimeTypesAllowed = entity.MimeTypesAllowed,
        MaxSizeBytes = entity.MaxSizeBytes,
    };
}
