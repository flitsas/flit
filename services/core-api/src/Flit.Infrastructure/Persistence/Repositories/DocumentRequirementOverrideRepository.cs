using Flit.Admin.Domain.DocumentRequirementOverrides;
using Flit.Infrastructure.Persistence.Entities.Tramites;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core de los overrides de obligatoriedad documental por OT (HU #10198).
///
/// <c>tramites.document_requirement_overrides</c> es parte del catálogo global SuperAdmin
/// (sin RLS). Todas las consultas usan EF LINQ parametrizado y enriquecen el read model con
/// el documento (join a <c>document_types</c>). El "limpiar" es un borrado físico.
/// </summary>
internal sealed class DocumentRequirementOverrideRepository : IDocumentRequirementOverrideRepository
{
    private readonly FlitDbContext _context;

    public DocumentRequirementOverrideRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<DocumentRequirementOverrideItem> UpsertAsync(
        Guid procedureTypeId,
        Guid documentTypeId,
        Guid transitOfficeId,
        string requirementState,
        Guid? actor,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.DocumentRequirementOverrides
            .FirstOrDefaultAsync(
                o => o.ProcedureTypeId == procedureTypeId
                    && o.DocumentTypeId == documentTypeId
                    && o.TransitOfficeId == transitOfficeId,
                cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            entity = new DocumentRequirementOverride
            {
                Id = Guid.NewGuid(),
                ProcedureTypeId = procedureTypeId,
                DocumentTypeId = documentTypeId,
                TransitOfficeId = transitOfficeId,
                RequirementState = requirementState,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = actor,
            };
            _context.DocumentRequirementOverrides.Add(entity);
        }
        else
        {
            entity.RequirementState = requirementState;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            entity.UpdatedBy = actor;
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await QueryById(entity.Id).FirstAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ClearAsync(
        Guid procedureTypeId,
        Guid documentTypeId,
        Guid transitOfficeId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.DocumentRequirementOverrides
            .FirstOrDefaultAsync(
                o => o.ProcedureTypeId == procedureTypeId
                    && o.DocumentTypeId == documentTypeId
                    && o.TransitOfficeId == transitOfficeId,
                cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return false;
        }

        _context.DocumentRequirementOverrides.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    public async Task<IReadOnlyList<DocumentRequirementOverrideItem>> ListByOtAsync(
        Guid procedureTypeId,
        Guid transitOfficeId,
        CancellationToken cancellationToken = default) =>
        await Enrich(
                _context.DocumentRequirementOverrides
                    .AsNoTracking()
                    .Where(o => o.ProcedureTypeId == procedureTypeId && o.TransitOfficeId == transitOfficeId))
            .OrderBy(o => o.DocumentName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private IQueryable<DocumentRequirementOverrideItem> QueryById(Guid id) =>
        Enrich(_context.DocumentRequirementOverrides.AsNoTracking().Where(o => o.Id == id));

    /// <summary>
    /// Proyecta el override enriquecido con los datos del documento (join a
    /// <c>document_types</c>) sobre el read model del dominio.
    /// </summary>
    private IQueryable<DocumentRequirementOverrideItem> Enrich(
        IQueryable<DocumentRequirementOverride> source) =>
        from o in source
        join d in _context.DocumentTypes.AsNoTracking() on o.DocumentTypeId equals d.Id
        select new DocumentRequirementOverrideItem
        {
            Id = o.Id,
            ProcedureTypeId = o.ProcedureTypeId,
            DocumentTypeId = o.DocumentTypeId,
            TransitOfficeId = o.TransitOfficeId,
            RequirementState = o.RequirementState,
            DocumentCode = d.Code,
            DocumentName = d.Name,
        };
}
