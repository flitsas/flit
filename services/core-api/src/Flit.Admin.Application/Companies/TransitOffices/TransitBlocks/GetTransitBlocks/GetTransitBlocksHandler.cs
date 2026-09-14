using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.TransitOffices.TransitBlocks.GetTransitBlocks;

/// <summary>Lectura de bloqueos de OT de una cabeza Marca Blanca (HU #12407 AC6).</summary>
public sealed class GetTransitBlocksHandler
{
    private readonly ITenantTransitOfficeBlockRepository _repository;

    public GetTransitBlocksHandler(ITenantTransitOfficeBlockRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<TransitBlocksResponse> HandleAsync(
        GetTransitBlocksQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var ids = await _repository
            .ListBlockedOfficeIdsAsync(query.HeadTenantId, cancellationToken)
            .ConfigureAwait(false);

        return new TransitBlocksResponse(ids);
    }
}
