using Flit.Admin.Domain.Plataforma.Ai;

namespace Flit.Admin.Application.Plataforma.Ai;

/// <summary>HU #13512 — listado de alertas in-app de failover.</summary>
public sealed class ListAiFailoverAlertsHandler
{
    private readonly IAiToolProviderSettingsRepository _repository;

    public ListAiFailoverAlertsHandler(IAiToolProviderSettingsRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<AiFailoverAlertsListView> HandleAsync(
        bool? acked,
        CancellationToken cancellationToken = default)
    {
        var rows = await _repository.ListAlertsAsync(acked, cancellationToken).ConfigureAwait(false);
        var items = rows.Select(Map).ToList();
        return new AiFailoverAlertsListView(items);
    }

    private static AiFailoverAlertView Map(AiFailoverAlert alert) =>
        new(
            alert.Id,
            alert.TransitionId,
            alert.ToolCode,
            alert.FromProvider,
            alert.ToProvider,
            alert.OccurredAt,
            Acked: alert.AckedAt is not null,
            alert.AckedAt,
            alert.AckedBy);
}
