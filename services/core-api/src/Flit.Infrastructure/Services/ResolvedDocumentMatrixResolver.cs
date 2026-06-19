using Flit.Admin.Domain.DocumentOrderOverrides;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Services;

/// <summary>
/// Implementación EF Core del resolutor de la matriz documental (HU #10196, RF18).
///
/// Algoritmo de precedencia <b>Cliente &gt; OT &gt; Default</b>:
/// (1) carga la base del trámite desde <c>procedure_document_requirements</c> (join a
/// <c>document_types</c>); (2) carga los overrides OT y CLIENTE solo cuando se aporta su
/// referencia; (3) por cada documento aplica el override de mayor precedencia disponible;
/// (4) ordena por orden resuelto asc, desempatando por <c>document_type_id</c>.
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
        Guid? clienteId,
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

        var otOverrides = await LoadOverridesAsync(
                procedureTypeId, DocumentOrderScope.Ot, transitOfficeId, cancellationToken)
            .ConfigureAwait(false);
        var clienteOverrides = await LoadOverridesAsync(
                procedureTypeId, DocumentOrderScope.Cliente, clienteId, cancellationToken)
            .ConfigureAwait(false);

        var resolved = new List<ResolvedDocumentMatrixItem>(baseDocs.Count);
        foreach (var doc in baseDocs)
        {
            short orden;
            string nivel;

            if (clienteOverrides.TryGetValue(doc.DocumentTypeId, out var clienteOrden))
            {
                orden = clienteOrden;
                nivel = DocumentOrderScope.Cliente;
            }
            else if (otOverrides.TryGetValue(doc.DocumentTypeId, out var otOrden))
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
                Obligatorio = doc.IsMandatory,
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

    private sealed record BaseDoc(Guid DocumentTypeId, string Code, string Name, bool IsMandatory, short DefaultSortOrder);
}
