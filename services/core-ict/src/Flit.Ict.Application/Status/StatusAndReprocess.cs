using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Enums;

namespace Flit.Ict.Application.Status;

public sealed record StatusView(string ManagerIdTransaction, string IctEstado, Guid? ProcedureInstanceId, string Comments);

/// <summary>Consulta el estado v2-native de un pre-trámite por su manager_id_transaction (TransactionFlit).</summary>
public sealed class StatusQueryHandler(IPreTramiteRepository repository, ICurrentTenant currentTenant)
{
    public async Task<(StatusView? Result, string? Error)> HandleAsync(
        string managerIdTransaction,
        CancellationToken ct = default)
    {
        var tenantId = currentTenant.TenantId;
        if (tenantId is null)
        {
            return (null, "unauthenticated");
        }

        var master = await repository.FindByManagerIdTransactionAsync(managerIdTransaction, tenantId.Value, ct);
        if (master is null)
        {
            return (null, "not_found");
        }

        var estado = IctEstado.Map(
            master.ProcessStatusId,
            master.ProcedureInstanceId is not null,
            master.BusinessValidation == 2,
            master.ExternalValidation > 0);

        var comments = string.Join(
            " ",
            new[] { master.BusinessCommentsValidation, master.ExternalCommentsValidation }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

        return (new StatusView(master.ManagerIdTransaction, estado, master.ProcedureInstanceId, comments), null);
    }
}

/// <summary>Reprocesa un pre-trámite en estado con_novedades: resetea las validaciones y el pipeline lo re-toma.</summary>
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
