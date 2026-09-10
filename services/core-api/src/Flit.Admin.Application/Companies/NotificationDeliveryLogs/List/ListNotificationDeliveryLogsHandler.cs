namespace Flit.Admin.Application.Companies.NotificationDeliveryLogs.List;

/// <summary>
/// Lista la bitácora de envíos de un tenant (HU #11363, AC3). El aislamiento multi-tenant lo aporta
/// <see cref="INotificationDeliveryLogRepository.ListByTenantAsync"/> con su <c>WHERE tenant_id</c>
/// explícito — este handler solo acota la paginación para que nadie pida una página gigante.
/// </summary>
public sealed class ListNotificationDeliveryLogsHandler
{
    private const int DefaultTake = 50;
    private const int MaxTake = 200;

    private readonly INotificationDeliveryLogRepository _repository;

    public ListNotificationDeliveryLogsHandler(INotificationDeliveryLogRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<IReadOnlyList<NotificationDeliveryLogResponse>> HandleAsync(
        Guid tenantId,
        int? skip,
        int? take,
        CancellationToken cancellationToken = default)
    {
        var effectiveSkip = skip is > 0 ? skip.Value : 0;
        var effectiveTake = take is > 0 ? Math.Min(take.Value, MaxTake) : DefaultTake;

        var rows = await _repository
            .ListByTenantAsync(tenantId, effectiveSkip, effectiveTake, cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(Map).ToList();
    }

    private static NotificationDeliveryLogResponse Map(NotificationDeliveryLogRecord r) =>
        new(r.Id, r.TemplateKey, r.Channel, r.Recipient, r.Result, r.FailureReason, r.DurationMs, r.OccurredAt);
}
