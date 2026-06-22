using Flit.Admin.Domain.DocumentOrderOverrides;
using Flit.Admin.Domain.DocumentRequirements;

namespace Flit.Admin.Application.DocumentOrderOverrides.GetResolvedDocumentMatrix;

/// <summary>
/// Caso de uso de la matriz documental resuelta (HU #10196, AC3/AC4 / RF18).
///
/// Flujo: (1) el tipo de trámite debe existir → 404; (2) delega la resolución de la
/// precedencia Cliente &gt; OT &gt; Default al <see cref="IResolvedDocumentMatrixResolver"/>;
/// un trámite existente sin documentos devuelve 200 con lista vacía.
/// </summary>
public sealed class GetResolvedDocumentMatrixHandler
{
    private readonly IProcedureTypeCatalog _procedureTypes;
    private readonly IResolvedDocumentMatrixResolver _resolver;

    public GetResolvedDocumentMatrixHandler(
        IProcedureTypeCatalog procedureTypes,
        IResolvedDocumentMatrixResolver resolver)
    {
        _procedureTypes = procedureTypes ?? throw new ArgumentNullException(nameof(procedureTypes));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public async Task<GetResolvedDocumentMatrixResult> HandleAsync(
        GetResolvedDocumentMatrixQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var procedureExists = await _procedureTypes
            .ExistsAsync(query.ProcedureTypeId, cancellationToken)
            .ConfigureAwait(false);
        if (!procedureExists)
        {
            return GetResolvedDocumentMatrixResult.ProcedureTypeNotFound;
        }

        var items = await _resolver
            .ResolveAsync(query.ProcedureTypeId, query.TransitOfficeId, query.ClienteId, cancellationToken)
            .ConfigureAwait(false);

        var data = items
            .Select(ResolvedDocumentMatrixResponse.From)
            .ToList();

        return GetResolvedDocumentMatrixResult.Resolved(data);
    }
}
