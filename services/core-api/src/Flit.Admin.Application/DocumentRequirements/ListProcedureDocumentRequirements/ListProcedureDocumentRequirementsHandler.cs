using Flit.Admin.Domain.DocumentRequirements;

namespace Flit.Admin.Application.DocumentRequirements.ListProcedureDocumentRequirements;

/// <summary>
/// Caso de uso del listado de documentos asociados a un tipo de trámite
/// (HU #10195, AC2 / RF06). Delega la consulta server-side (orden por
/// <c>default_sort_order</c> asc, enriquecida con el documento) al repositorio.
/// </summary>
public sealed class ListProcedureDocumentRequirementsHandler
{
    private readonly IProcedureDocumentRequirementRepository _repository;

    public ListProcedureDocumentRequirementsHandler(IProcedureDocumentRequirementRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<ListProcedureDocumentRequirementsResult> HandleAsync(
        ListProcedureDocumentRequirementsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var items = await _repository
            .ListByProcedureTypeAsync(query.ProcedureTypeId, cancellationToken)
            .ConfigureAwait(false);

        var data = items
            .Select(ProcedureDocumentRequirementResponse.From)
            .ToList();

        return new ListProcedureDocumentRequirementsResult(data);
    }
}
