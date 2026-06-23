using Flit.Admin.Domain.OtDocumentPrecedence;

namespace Flit.Admin.Application.OtDocumentPrecedence.ListOtDocumentPrecedence;

public sealed class ListOtDocumentPrecedenceQuery
{
    public Guid TenantId { get; init; }

    public Guid ProcedureTypeId { get; init; }
}

public sealed class ListOtDocumentPrecedenceResult
{
    public IReadOnlyList<OtDocumentPrecedenceResponse> Data { get; init; } = Array.Empty<OtDocumentPrecedenceResponse>();
}

/// <summary>Lista prelación documental por tipo de trámite (HU #10222 AC1).</summary>
public sealed class ListOtDocumentPrecedenceHandler
{
    private readonly IOtDocumentPrecedenceRepository _repository;

    public ListOtDocumentPrecedenceHandler(IOtDocumentPrecedenceRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<ListOtDocumentPrecedenceResult> HandleAsync(
        ListOtDocumentPrecedenceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var items = await _repository.ListByProcedureTypeAsync(
            query.TenantId,
            query.ProcedureTypeId,
            cancellationToken).ConfigureAwait(false);

        return new ListOtDocumentPrecedenceResult
        {
            Data = items.Select(OtDocumentPrecedenceMapper.ToResponse).ToList(),
        };
    }
}
