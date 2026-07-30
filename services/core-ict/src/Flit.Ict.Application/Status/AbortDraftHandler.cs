using Flit.Ict.Domain.Abstractions;

namespace Flit.Ict.Application.Status;

/// <summary>
/// Anula un trámite de integración (servicio v1 <c>abortProcess</c>). Resuelve el pre-trámite por su
/// manager_id_transaction y actúa según su etapa:
/// <list type="bullet">
/// <item>YA materializado (tiene <c>procedure_instance_id</c>): delega la anulación en core-api
/// (gRPC <c>AbortDraft</c>, estado <c>anulado</c>) y refleja el resultado en el histórico ICT.</item>
/// <item>AÚN en el pipeline (sin borrador): aborta el pre-trámite en ICT (<c>process_status_id=6</c>
/// ANULADO), lo que lo saca de los jobs (negocio/externo/envío) y corta el pipeline. Paridad con v1,
/// donde <c>abortProcess</c> aplicaba a un trámite aún no enviado al OT.</item>
/// </list>
/// </summary>
public sealed class AbortDraftHandler(
    IPreTramiteRepository repository,
    IProcedureDraftClient draftClient,
    ICurrentTenant currentTenant)
{
    /// <summary>Código interno v1 de estado ANULADO en <c>ict.external_integration_master.process_status_id</c>.</summary>
    private const int AnuladoStatus = 6;


    public async Task<(bool Ok, string? Error)> HandleAsync(
        string managerIdTransaction,
        string? observation,
        CancellationToken ct = default)
    {
        var tenantId = currentTenant.TenantId;
        if (tenantId is null)
        {
            return (false, "unauthenticated");
        }

        var master = await repository.FindByManagerIdTransactionAsync(managerIdTransaction, tenantId.Value, ct);
        if (master is null)
        {
            return (false, "not_found");
        }

        if (master.ProcedureInstanceId is null)
        {
            // El pre-trámite aún no materializó en core-api: no hay borrador que anular allí, pero SÍ se
            // puede abortar en el pipeline de ICT (v1 permitía abortProcess sobre un trámite no enviado).
            // Idempotencia: si ya está ANULADO (6) no se re-aborta.
            if (master.ProcessStatusId == AnuladoStatus)
            {
                return (false, "estado_final");
            }

            // MarkAbortedAsync deja el master en process_status_id=6 (ANULADO): los jobs de negocio/
            // externo/envío filtran por otros estados, así que deja de avanzar (pipeline cortado).
            await repository.MarkAbortedAsync(
                master.Id, tenantId.Value, observation ?? string.Empty,
                master.ManagerUser, master.ManagerMail, master.CompanyManagerDocument, ct);

            // Paridad v1 (abortProcess/WithNews): avisar al gestor. Como este pre-trámite NO pasó por
            // core-api, el Plano C no dispara ningún webhook; se encola aquí. Tras MarkAborted para no
            // notificar una anulación que no se persistió.
            await repository.EnqueueAbortWebhookAsync(
                master.Id, tenantId.Value, observation ?? string.Empty, ct);
            return (true, null);
        }

        var result = await draftClient.AbortDraftAsync(
            tenantId.Value,
            master.ProcedureInstanceId.Value,
            observation ?? string.Empty,
            master.ManagerUser,
            master.ManagerMail,
            master.CompanyManagerDocument,
            ct);

        if (result.ErrorCode is not null)
        {
            return (false, result.ErrorCode);
        }

        await repository.MarkAbortedAsync(
            master.Id, tenantId.Value, observation ?? string.Empty,
            master.ManagerUser, master.ManagerMail, master.CompanyManagerDocument, ct);
        return (true, null);
    }
}
