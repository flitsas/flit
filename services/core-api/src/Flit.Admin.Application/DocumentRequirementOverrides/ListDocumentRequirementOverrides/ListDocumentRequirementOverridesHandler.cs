using Flit.Admin.Domain.DocumentRequirementOverrides;

namespace Flit.Admin.Application.DocumentRequirementOverrides.ListDocumentRequirementOverrides;

/// <summary>
/// Caso de uso del listado de overrides de obligatoriedad de un trámite para un OT
/// (HU #10198). Devuelve solo las filas con override; la UI combina con las asociaciones del
/// trámite para mostrar el estado efectivo (las no listadas heredan el default).
/// </summary>
public sealed class ListDocumentRequirementOverridesHandler
{
    private readonly IDocumentRequirementOverrideRepository _repository;

    public ListDocumentRequirementOverridesHandler(IDocumentRequirementOverrideRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<ListDocumentRequirementOverridesResult> HandleAsync(
        ListDocumentRequirementOverridesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var items = await _repository
            .ListByOtAsync(query.ProcedureTypeId, query.TransitOfficeId, cancellationToken)
            .ConfigureAwait(false);

        var data = items
            .Select(DocumentRequirementOverrideResponse.From)
            .ToList();

        return new ListDocumentRequirementOverridesResult(data);
    }
}
