using Flit.Ict.Domain.Enums;
using Flit.Ict.Grpc.Contracts;
using Flit.Ict.Infrastructure.Persistence;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace Flit.Ict.Api.Grpc;

/// <summary>
/// Servidor gRPC del callback de estados: core-api hace push cuando cambia el estado de un trámite
/// con origin='ict'. Por cada notificación:
/// <list type="number">
///   <item>Mapea <c>ToStatus</c> (vocabulario v2) al código numérico v1.</item>
///   <item>Llama <c>ict.record_process_status</c> para agregar o refrescar el histórico de estados.</item>
///   <item>Actualiza <c>process_status_id</c> del master para estados OT (7-13) de modo que
///       <c>statusValidation</c> refleje el último estado del trámite materializado.</item>
///   <item>Encola un webhook con vocabulario v2 para que el job de notificación lo entregue al gestor.</item>
/// </list>
/// El GUC <c>app.current_tenant_id</c> se fija LOCAL a la transacción para satisfacer RLS en ambas
/// tablas (<c>external_integration_master</c> y <c>external_integration_webhook_master</c>).
/// </summary>
public sealed partial class IctStateCallbackService(IctDbContext db, ILogger<IctStateCallbackService> logger)
    : IctStateCallback.IctStateCallbackBase
{
    public override async Task<Ack> NotifyProcedureStateChanged(StateChangeNotification request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        Log.StateChange(logger, request.ExternalRef, request.FromStatus, request.ToStatus);

        if (!Guid.TryParse(request.ExternalRef, out var masterId) || !Guid.TryParse(request.TenantId, out var tenantId))
        {
            return new Ack { Received = true };
        }

        var v1Code = IctProcessStatusCodes.MapFromV2(request.ToStatus);
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? request.ToStatus : request.Reason;
        var ct = context.CancellationToken;

        // Toda la mutación va en UNA transacción con GUC local para satisfacer RLS en las tablas
        // ict.external_integration_master y ict.external_integration_webhook_master.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // GUC local a la transacción (is_local=true): se revierte automáticamente al commit/rollback
        // sin necesidad de limpiarlo manualmente. Mismo mecanismo que PreTramiteRepository.InTenantTransactionAsync.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)", ct);

        // 1. Registrar en el histórico de estados v1 (append / refresh del último).
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT ict.record_process_status(
                {masterId}, {tenantId}, {v1Code}, {reason},
                '', '', '', '', 'callback')
            """, ct);

        // 2. Actualizar process_status_id del master para estados OT (preparado→revocado, 7-13) de modo
        //    que IctStatusV2Query y statusValidation reflejen el estado actual del trámite.
        if (IctProcessStatusCodes.IsOtState(v1Code))
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE ict.external_integration_master
                SET process_status_id = {v1Code}
                WHERE id = {masterId} AND tenant_id = {tenantId}
                """, ct);
        }

        // 3. Encolar webhook con el código v1 correcto (antes siempre era 3=Procesado; ahora refleja el
        //    estado real). El job de notificación lo entrega con vocabulario v2 (ict_estado) + compat v1
        //    (status numérico + statusDescription usando IctProcessStatusCodes.Describe).
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ict.external_integration_webhook_master
                (id_transaction, tenant_id, manager_id_transaction, transaction_type, status_validation,
                 message_validation, ict_estado, target_url)
            SELECT m.id, m.tenant_id, m.manager_id_transaction, m.transaction_type, {v1Code},
                   {reason}, {request.ToStatus}, m.url_web_hook
            FROM ict.external_integration_master m
            WHERE m.id = {masterId} AND m.tenant_id = {tenantId}
            """, ct);

        await tx.CommitAsync(ct);

        return new Ack { Received = true };
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "ICT state callback: ext_ref={ExternalRef} {From} -> {To}")]
        public static partial void StateChange(ILogger logger, string externalRef, string from, string to);
    }
}
