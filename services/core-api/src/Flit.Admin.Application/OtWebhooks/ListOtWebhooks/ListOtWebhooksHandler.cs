using Flit.Admin.Domain.OtWebhooks;

namespace Flit.Admin.Application.OtWebhooks.ListOtWebhooks;

/// <summary>Lista suscripciones webhook OT del tenant (soporte HU #10219 AC1).</summary>
public sealed class ListOtWebhooksHandler
{
    private readonly IOtWebhookSubscriptionRepository _repository;

    public ListOtWebhooksHandler(IOtWebhookSubscriptionRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<ListOtWebhooksResult> HandleAsync(
        ListOtWebhooksQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var items = await _repository
            .ListAllAsync(query.TenantId, cancellationToken)
            .ConfigureAwait(false);

        return new ListOtWebhooksResult
        {
            Data = items.Select(OtWebhookMapper.ToResponse).ToList(),
        };
    }
}
