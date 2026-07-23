using Flit.Ict.Domain.Abstractions;

namespace Flit.Ict.Application.Status;

/// <summary>Reprocesa un trámite en estado con_novedades: resetea las validaciones y el pipeline lo re-toma.</summary>
public sealed class ReprocessHandler(IPreTramiteRepository repository, ICurrentTenant currentTenant)
{
    public async Task<(bool Ok, string? Error)> HandleAsync(string managerIdTransaction, CancellationToken ct = default)
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

        if (master.ProcedureInstanceId is not null)
        {
            return (false, "already_materialized");
        }

        if (master.ProcessStatusId != 4)
        {
            return (false, "not_in_novelty");
        }

        master.ProcessStatusId = 1;
        master.BusinessValidation = 0;
        master.ExternalValidation = 0;
        master.BusinessCommentsValidation = string.Empty;
        master.ExternalCommentsValidation = string.Empty;
        master.UpdatedBy = currentTenant.IntegrationClientId;
        await repository.SaveAsync(tenantId.Value, ct);
        return (true, null);
    }
}
