using Flit.Ict.Grpc.Contracts;
using Flit.Ict.Infrastructure.Persistence;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace Flit.Ict.Api.Grpc;

/// <summary>
/// Servidor gRPC del callback de estados: core-api hace push cuando cambia el estado de un trámite
/// con origin='ict'. Resuelve el pre-trámite por external_ref y encola un webhook con el vocabulario
/// v2 para que el job de notificación lo entregue al gestor.
/// </summary>
public sealed partial class IctStateCallbackService(IctDbContext db, ILogger<IctStateCallbackService> logger)
    : IctStateCallback.IctStateCallbackBase
{
    public override async Task<Ack> NotifyProcedureStateChanged(StateChangeNotification request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        Log.StateChange(logger, request.ExternalRef, request.FromStatus, request.ToStatus);

        if (Guid.TryParse(request.ExternalRef, out var masterId) && Guid.TryParse(request.TenantId, out var tenantId))
        {
            var reason = string.IsNullOrWhiteSpace(request.Reason) ? request.ToStatus : request.Reason;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO ict.external_integration_webhook_master
                    (id_transaction, tenant_id, manager_id_transaction, transaction_type, status_validation,
                     message_validation, ict_estado, target_url)
                SELECT m.id, m.tenant_id, m.manager_id_transaction, m.transaction_type, 3,
                       {reason}, {request.ToStatus}, m.url_web_hook
                FROM ict.external_integration_master m
                WHERE m.id = {masterId} AND m.tenant_id = {tenantId}
                """, context.CancellationToken);
        }

        return new Ack { Received = true };
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "ICT state callback: ext_ref={ExternalRef} {From} -> {To}")]
        public static partial void StateChange(ILogger logger, string externalRef, string from, string to);
    }
}
