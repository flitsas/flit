using Flit.Admin.Domain.DocumentOrderOverrides;
using Flit.Admin.Domain.DocumentRequirementOverrides;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Services;

/// <summary>
/// Implementación EF Core del resolutor de la matriz documental (HU #10196, RF18; HU #10198).
///
/// Algoritmo de precedencia del <b>orden</b> <b>OT &gt; Default</b> (RF22 — el organismo de
/// tránsito es el ÚNICO nivel que sugiere el orden; se retiró el nivel Cliente y el override
/// de orden general/por cliente): (1) carga la base del trámite desde
/// <c>procedure_document_requirements</c> (join a <c>document_types</c>); (2) carga los
/// overrides OT solo cuando se aporta su referencia; (3) por cada documento aplica el override
/// OT si existe, si no el orden base del trámite; (4) ordena por orden resuelto asc,
/// desempatando por <c>document_type_id</c>.
///
/// Para el scope OT, la prelación de <c>admin.ot_document_precedence</c> (consola OT HU #10222)
/// tiene prioridad sobre <c>tramites.document_order_overrides</c> (HU #10196).
///
/// Además aplica la <b>obligatoriedad por OT</b> (HU #10198, granular solo para OT): cuando
/// se aporta el OT, un override de obligatoriedad puede marcar el documento como obligatorio
/// (<c>REQUIRED</c>), opcional (<c>OPTIONAL</c>) o no aplica (<c>NOT_APPLICABLE</c> → el
/// documento se excluye de la matriz de ese OT). Sin override, hereda el default del trámite.
/// </summary>
internal sealed class ResolvedDocumentMatrixResolver : IResolvedDocumentMatrixResolver
{
    private readonly FlitDbContext _context;

    public ResolvedDocumentMatrixResolver(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<ResolvedDocumentMatrixItem>> ResolveAsync(
        Guid procedureTypeId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default)
    {
        var baseDocs = await (
                from r in _context.ProcedureDocumentRequirements.AsNoTracking()
                where r.ProcedureTypeId == procedureTypeId
                join d in _context.DocumentTypes.AsNoTracking() on r.DocumentTypeId equals d.Id
                select new BaseDoc(d.Id, d.Code, d.Name, r.IsMandatory, r.DefaultSortOrder))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (baseDocs.Count == 0)
        {
            return [];
        }

        var otLegacyOverrides = await LoadOverridesAsync(
                procedureTypeId, DocumentOrderScope.Ot, transitOfficeId, cancellationToken)
            .ConfigureAwait(false);
        var otAdminPrecedence = await LoadOtDocumentPrecedenceAsync(
                procedureTypeId, transitOfficeId, cancellationToken)
            .ConfigureAwait(false);
        var otOverrides = MergeOtOrder(otLegacyOverrides, otAdminPrecedence);
        var otRequirementStates = await LoadOtRequirementStatesAsync(
                procedureTypeId, transitOfficeId, cancellationToken)
            .ConfigureAwait(false);

        var resolved = new List<ResolvedDocumentMatrixItem>(baseDocs.Count);
        foreach (var doc in baseDocs)
        {
            // Obligatoriedad por OT (HU #10198): NOT_APPLICABLE excluye el documento de la
            // matriz de ese OT; REQUIRED/OPTIONAL fijan la obligatoriedad; sin override hereda.
            var obligatorio = doc.IsMandatory;
            if (otRequirementStates.TryGetValue(doc.DocumentTypeId, out var requirementState))
            {
                if (requirementState == DocumentRequirementState.NotApplicable)
                {
                    continue;
                }

                obligatorio = requirementState == DocumentRequirementState.Required;
            }

            // RF22 — el OT es el único nivel que reordena; sin override OT se usa el orden base
            // del trámite (no configurable por cliente ni por un override de orden general).
            short orden;
            string nivel;

            if (otOverrides.TryGetValue(doc.DocumentTypeId, out var otOrden))
            {
                orden = otOrden;
                nivel = DocumentOrderScope.Ot;
            }
            else
            {
                orden = doc.DefaultSortOrder;
                nivel = DocumentOrderScope.Default;
            }

            resolved.Add(new ResolvedDocumentMatrixItem
            {
                DocumentTypeId = doc.DocumentTypeId,
                Codigo = doc.Code,
                Nombre = doc.Name,
                Obligatorio = obligatorio,
                OrdenResuelto = orden,
                NivelAplicado = nivel,
            });
        }

        return [.. resolved.OrderBy(x => x.OrdenResuelto).ThenBy(x => x.DocumentTypeId)];
    }

    /// <summary>
    /// Carga los overrides de un scope indexados por documento. Si la referencia no viene
    /// (nivel omitido) devuelve un diccionario vacío.
    /// </summary>
    private async Task<Dictionary<Guid, short>> LoadOverridesAsync(
        Guid procedureTypeId,
        string scopeType,
        Guid? scopeRefId,
        CancellationToken cancellationToken)
    {
        if (scopeRefId is null || scopeRefId == Guid.Empty)
        {
            return [];
        }

        return await _context.DocumentOrderOverrides
            .AsNoTracking()
            .Where(o => o.ProcedureTypeId == procedureTypeId
                && o.ScopeType == scopeType
                && o.ScopeRefId == scopeRefId.Value)
            .ToDictionaryAsync(o => o.DocumentTypeId, o => o.SortOrder, cancellationToken)
            .ConfigureAwait(false);
    }

    private static Dictionary<Guid, short> MergeOtOrder(
        Dictionary<Guid, short> legacy,
        Dictionary<Guid, short> adminPrecedence)
    {
        if (adminPrecedence.Count == 0)
        {
            return legacy;
        }

        var merged = new Dictionary<Guid, short>(legacy);
        foreach (var (documentTypeId, sortOrder) in adminPrecedence)
        {
            merged[documentTypeId] = sortOrder;
        }

        return merged;
    }

    private async Task<Dictionary<Guid, short>> LoadOtDocumentPrecedenceAsync(
        Guid procedureTypeId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken)
    {
        if (transitOfficeId is null || transitOfficeId == Guid.Empty)
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

        return await _context.OtDocumentPrecedences
            .AsNoTracking()
            .Where(p => p.TenantId == otTenantId && p.ProcedureTypeId == procedureTypeId)
            .ToDictionaryAsync(p => p.DocumentTypeId, p => p.SortOrder, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Carga los estados de obligatoriedad por OT indexados por documento (HU #10198). Si no
    /// se aporta el OT devuelve un diccionario vacío (sin override de obligatoriedad).
    /// </summary>
    private async Task<Dictionary<Guid, string>> LoadOtRequirementStatesAsync(
        Guid procedureTypeId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken)
    {
        if (transitOfficeId is null || transitOfficeId == Guid.Empty)
        {
            return [];
        }

        return await _context.DocumentRequirementOverrides
            .AsNoTracking()
            .Where(o => o.ProcedureTypeId == procedureTypeId
                && o.TransitOfficeId == transitOfficeId.Value)
            .ToDictionaryAsync(o => o.DocumentTypeId, o => o.RequirementState, cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed record BaseDoc(Guid DocumentTypeId, string Code, string Name, bool IsMandatory, short DefaultSortOrder);
}
