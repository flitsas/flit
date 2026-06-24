using Flit.Admin.Domain.OtDocumentTags;

namespace Flit.Admin.Application.OtDocumentTags.ListOtDocumentTags;

public sealed class ListOtDocumentTagsQuery
{
    public Guid TenantId { get; init; }
}

public sealed class ListOtDocumentTagsResult
{
    public IReadOnlyList<OtDocumentTagResponse> Data { get; init; } = Array.Empty<OtDocumentTagResponse>();
}

/// <summary>Lista etiquetas documentales del tenant (HU #10222 AC5).</summary>
public sealed class ListOtDocumentTagsHandler
{
    private readonly IOtDocumentTagRepository _repository;

    public ListOtDocumentTagsHandler(IOtDocumentTagRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<ListOtDocumentTagsResult> HandleAsync(
        ListOtDocumentTagsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var tags = await _repository.ListByTenantAsync(query.TenantId, cancellationToken).ConfigureAwait(false);
        return new ListOtDocumentTagsResult
        {
            Data = tags.Select(OtDocumentTagMapper.ToResponse).ToList(),
        };
    }
}
