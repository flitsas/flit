using Flit.Ict.Domain.Abstractions;

namespace Flit.Ict.Application.Status;

/// <summary>
/// Anula un trámite ya materializado (servicio v1 <c>abortProcess</c>). Resuelve el pre-trámite por su
/// manager_id_transaction, delega la anulación en core-api (gRPC, estado <c>anulado</c>) y refleja el
/// resultado en el histórico ICT. Solo aplica si el pre-trámite ya tiene borrador (procedure_instance).
/// </summary>
public sealed class AbortDraftHandler(
    IPreTramiteRepository repository,
    IProcedureDraftClient draftClient,
    ICurrentTenant currentTenant)
{
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
            // El trámite aún no llegó a borrador en core-api: no hay nada que anular allí.
            return (false, "not_materialized");
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
