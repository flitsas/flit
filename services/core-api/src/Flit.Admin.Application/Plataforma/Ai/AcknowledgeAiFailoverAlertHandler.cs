using Flit.Admin.Domain.Plataforma.Ai;

namespace Flit.Admin.Application.Plataforma.Ai;

/// <summary>HU #13512 — ack de alerta de failover (claims <c>sub</c>).</summary>
public sealed class AcknowledgeAiFailoverAlertHandler
{
    private readonly IAiToolProviderSettingsRepository _repository;

    public AcknowledgeAiFailoverAlertHandler(IAiToolProviderSettingsRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<AiFailoverAlertView?> HandleAsync(
        Guid alertId,
        Guid? ackedBy,
        CancellationToken cancellationToken = default)
    {
        var alert = await _repository
            .AcknowledgeAlertAsync(alertId, ackedBy, cancellationToken)
            .ConfigureAwait(false);

        if (alert is null)
        {
            return null;
        }

        return new AiFailoverAlertView(
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
}
