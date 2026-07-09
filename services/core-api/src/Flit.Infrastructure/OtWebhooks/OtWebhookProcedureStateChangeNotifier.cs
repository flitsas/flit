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

        // N 03 (RF05): el motivo de la transición viaja a la OT; estados en el vocabulario
        // de negocio (borrador|anulado|preparado|entregado|aprobado|rechazado — ADR-0022).
        var payload = new
        {
            event_type = OtWebhookEventTypes.VehicleStateChanged,
            procedure_instance_id = change.ProcedureInstanceId,
            from_status = change.FromStatus,
            to_status = change.ToStatus,
            changed_at = change.ChangedAt,
            reason = change.Reason,
        };

        return _dispatchService.DispatchAsync(
            change.TenantId,
            OtWebhookEventTypes.VehicleStateChanged,
            payload,
            cancellationToken);
    }
}
