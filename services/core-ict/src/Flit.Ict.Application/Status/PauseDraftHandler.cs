using Flit.Ict.Domain.Abstractions;

namespace Flit.Ict.Application.Status;

/// <summary>
/// Pausa o reanuda un trámite ya materializado (servicio v1 <c>pauseDraftProcess</c>). Resuelve el
/// pre-trámite por su manager_id_transaction y delega en core-api (gRPC <c>PauseDraft</c>), que persiste
/// la pausa en el borrador. Solo aplica si el pre-trámite ya tiene borrador (procedure_instance).
/// </summary>
public sealed class PauseDraftHandler(
    IPreTramiteRepository repository,
    IProcedureDraftClient draftClient,
    ICurrentTenant currentTenant)
{
    public async Task<(bool Ok, string? Error)> HandleAsync(
        string managerIdTransaction,
        bool paused,
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
            return (false, "not_materialized");
        }

        var result = await draftClient.PauseDraftAsync(
            tenantId.Value,
            master.ProcedureInstanceId.Value,
            paused,
            observation ?? string.Empty,
            master.ManagerUser,
            master.ManagerMail,
            master.CompanyManagerDocument,
            ct);

        return result.ErrorCode is not null ? (false, result.ErrorCode) : (true, null);
    }
}
