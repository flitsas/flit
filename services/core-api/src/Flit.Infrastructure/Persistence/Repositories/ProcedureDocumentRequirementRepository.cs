using Flit.Admin.Domain.DocumentRequirements;
using Flit.Infrastructure.Persistence.Entities.Tramites;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core de la asociación trámite ↔ tipo de documento (HU #10195).
///
/// <c>tramites.procedure_document_requirements</c> es parte del catálogo global
/// SuperAdmin (sin RLS). Todas las consultas usan EF LINQ parametrizado y enriquecen
/// el read model con el documento asociado (join a <c>document_types</c>). El borrado
/// (AC4) es físico.
/// </summary>
internal sealed class ProcedureDocumentRequirementRepository : IProcedureDocumentRequirementRepository
{
    private readonly FlitDbContext _context;

    public ProcedureDocumentRequirementRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<ProcedureDocumentRequirementItem> CreateAsync(
        Guid procedureTypeId,
        Guid documentTypeId,
        short ordenDefault,
        bool obligatorio,
        Guid? createdBy,
        CancellationToken cancellationToken = default)
    {
        var entity = new ProcedureDocumentRequirement
        {
            Id = Guid.NewGuid(),
            ProcedureTypeId = procedureTypeId,
            DocumentTypeId = documentTypeId,
            DefaultSortOrder = ordenDefault,
            IsMandatory = obligatorio,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = createdBy,
        };

        _context.ProcedureDocumentRequirements.Add(entity);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var created = await QueryById(entity.Id).FirstAsync(cancellationToken).ConfigureAwait(false);
        return created;
    }

    public async Task<IReadOnlyList<ProcedureDocumentRequirementItem>> ListByProcedureTypeAsync(
        Guid procedureTypeId,
        CancellationToken cancellationToken = default)
    {
        var items = await Enrich(
                _context.ProcedureDocumentRequirements
                    .AsNoTracking()
                    .Where(r => r.ProcedureTypeId == procedureTypeId))
            .OrderBy(r => r.OrdenDefault)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return items;
    }

    public Task<ProcedureDocumentRequirementItem?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        QueryById(id).FirstOrDefaultAsync(cancellationToken);

    public async Task<ProcedureDocumentRequirementItem?> UpdateAsync(
        Guid id,
        short ordenDefault,
        bool obligatorio,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.ProcedureDocumentRequirements
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return null;
        }

        entity.DefaultSortOrder = ordenDefault;
        entity.IsMandatory = obligatorio;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await QueryById(id).FirstAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.ProcedureDocumentRequirements
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return false;
        }

        _context.ProcedureDocumentRequirements.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    public Task<bool> ExistsPairAsync(
        Guid procedureTypeId,
        Guid documentTypeId,
        CancellationToken cancellationToken = default) =>
        _context.ProcedureDocumentRequirements
            .AsNoTracking()
            .AnyAsync(
                r => r.ProcedureTypeId == procedureTypeId && r.DocumentTypeId == documentTypeId,
                cancellationToken);

    private IQueryable<ProcedureDocumentRequirementItem> QueryById(Guid id) =>
        Enrich(_context.ProcedureDocumentRequirements.AsNoTracking().Where(r => r.Id == id));

    /// <summary>
    /// Proyecta la asociación enriquecida con los datos del documento asociado
    /// (join a <c>document_types</c>) sobre el read model del dominio.
    /// </summary>
    private IQueryable<ProcedureDocumentRequirementItem> Enrich(
        IQueryable<ProcedureDocumentRequirement> source) =>
        from r in source
        join d in _context.DocumentTypes.AsNoTracking() on r.DocumentTypeId equals d.Id
        select new ProcedureDocumentRequirementItem
        {
            Id = r.Id,
            ProcedureTypeId = r.ProcedureTypeId,
            DocumentTypeId = r.DocumentTypeId,
            OrdenDefault = r.DefaultSortOrder,
            Obligatorio = r.IsMandatory,
            DocumentCode = d.Code,
            DocumentName = d.Name,
            DocumentIsActive = d.IsActive,
        };
}
