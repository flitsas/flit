using Flit.Admin.Domain.DocumentRequirements;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core de <see cref="IProcedureTypeCatalog"/> (HU #10195). Consulta
/// de solo lectura sobre <c>tramites.procedure_types</c> para validar la existencia de
/// un tipo de trámite al asociar documentos.
/// </summary>
internal sealed class ProcedureTypeCatalog : IProcedureTypeCatalog
{
    private readonly FlitDbContext _context;

    public ProcedureTypeCatalog(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<bool> ExistsAsync(
        Guid procedureTypeId,
        CancellationToken cancellationToken = default) =>
        _context.ProcedureTypes
            .AsNoTracking()
            .AnyAsync(p => p.Id == procedureTypeId, cancellationToken);
}
