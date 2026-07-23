using Flit.Ict.Grpc.Contracts;
using Grpc.Core;

namespace Flit.Ict.Api.Grpc;

/// <summary>
/// Servidor gRPC del callback de estados: core-api hace push cuando cambia el estado de un trámite
/// con origin='ict'. En HU4 se resuelve el master por external_ref y se encola el webhook al gestor.
/// </summary>
public sealed partial class IctStateCallbackService(ILogger<IctStateCallbackService> logger)
    : IctStateCallback.IctStateCallbackBase
{
    public override Task<Ack> NotifyProcedureStateChanged(StateChangeNotification request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        Log.StateChange(logger, request.ExternalRef, request.FromStatus, request.ToStatus);

        // TODO(ICT-HU4): resolver el master por external_ref, proyectar el estado v2 y encolar el
        // webhook al gestor (ict.external_integration_webhook_master).
        return Task.FromResult(new Ack { Received = true });
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "ICT state callback: ext_ref={ExternalRef} {From} -> {To}")]
        public static partial void StateChange(ILogger logger, string externalRef, string from, string to);
    }
}
