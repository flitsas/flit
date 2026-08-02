using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Services;

/// <summary>
/// Lee el orden documental que el OT guardó en <c>admin.ot_document_precedence</c> (HU #11184) y lo
/// devuelve como códigos del catálogo, listo para ordenar el expediente.
/// <para>
/// Solo devuelve filas cuando el organismo configuró explícitamente su prelación: la lista vacía es
/// la señal de respaldo que hace que el consolidado conserve el orden por modalidad de siempre.
/// </para>
/// <para>
/// El tenant se resuelve desde <c>admin.transit_office_profiles</c> —igual que en
/// <see cref="ResolvedDocumentMatrixResolver"/>— porque el trámite se genera en el tenant del
/// cliente pero la prelación pertenece al del organismo.
/// </para>
/// </summary>
internal sealed class OtConfiguredDocumentOrderProvider : IOtConfiguredDocumentOrderProvider
{
    private readonly FlitDbContext _context;

    public OtConfiguredDocumentOrderProvider(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<string>> GetConfiguredOrderAsync(
        Guid procedureTypeId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default)
    {
        if (procedureTypeId == Guid.Empty || transitOfficeId is null || transitOfficeId == Guid.Empty)
        {
            return [];
        }

        var otTenantId = await _context.TransitOfficeProfiles
            .AsNoTracking()
            .Where(p => p.TransitOfficeId == transitOfficeId.Value)
            .Select(p => (Guid?)p.TenantId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (otTenantId is null)
        {
            return [];
        }

        return await (
                from p in _context.OtDocumentPrecedences.AsNoTracking()
                where p.TenantId == otTenantId && p.ProcedureTypeId == procedureTypeId
                join d in _context.DocumentTypes.AsNoTracking() on p.DocumentTypeId equals d.Id
                orderby p.SortOrder, d.Code
                select d.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
