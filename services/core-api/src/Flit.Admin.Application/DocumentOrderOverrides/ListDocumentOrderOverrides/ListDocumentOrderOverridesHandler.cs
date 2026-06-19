using Flit.Admin.Domain.DocumentOrderOverrides;

namespace Flit.Admin.Application.DocumentOrderOverrides.ListDocumentOrderOverrides;

/// <summary>
/// Caso de uso del listado de overrides por scope (HU #10196, AC5 / RF09–RF16). Delega la
/// consulta server-side (filtrada por trámite/scope/referencia, ordenada por
/// <c>sort_order</c> asc y enriquecida con el documento) al repositorio.
/// </summary>
public sealed class ListDocumentOrderOverridesHandler
{
    private readonly IDocumentOrderOverrideRepository _repository;

    public ListDocumentOrderOverridesHandler(IDocumentOrderOverrideRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<ListDocumentOrderOverridesResult> HandleAsync(
        ListDocumentOrderOverridesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var items = await _repository
            .ListByScopeAsync(query.ProcedureTypeId, query.Scope, query.ScopeRefId, cancellationToken)
            .ConfigureAwait(false);

        var data = items
            .Select(DocumentOrderOverrideResponse.From)
            .ToList();

        return new ListDocumentOrderOverridesResult(data);
    }
}
