using Flit.Admin.Domain.OtWebhooks;
using Flit.Tramites.Domain.Integration;

namespace Flit.Infrastructure.OtWebhooks;

/// <summary>
/// Puente de cambios de estado de trámite hacia webhooks OT (HU #10216 AC2).
/// </summary>
internal sealed class OtWebhookProcedureStateChangeNotifier : IProcedureStateChangeNotifier
{
    private readonly IOtWebhookDispatchService _dispatchService;

    public OtWebhookProcedureStateChangeNotifier(IOtWebhookDispatchService dispatchService)
    {
        _dispatchService = dispatchService ?? throw new ArgumentNullException(nameof(dispatchService));
    }

    public Task NotifyAsync(ProcedureStateChangeEvent change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        var payload = new
        {
            event_type = OtWebhookEventTypes.VehicleStateChanged,
            procedure_instance_id = change.ProcedureInstanceId,
            from_status = change.FromStatus,
            to_status = change.ToStatus,
            changed_at = change.ChangedAt,
        };

        return _dispatchService.DispatchAsync(
            change.TenantId,
            OtWebhookEventTypes.VehicleStateChanged,
            payload,
            cancellationToken);
    }
}
