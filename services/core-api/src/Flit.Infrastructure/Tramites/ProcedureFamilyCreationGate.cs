using Flit.Admin.Domain.Companies.Settings;
using Flit.Tramites.Domain.Integration;

namespace Flit.Infrastructure.Tramites;

/// <summary>
/// Lee <c>admin.tenant_operational_policies</c> y aplica el bloqueo por familia
/// (MATRICULAS = NOT allow_initial_registration; TRASPASO/OTROS = columnas block_*).
/// </summary>
internal sealed class ProcedureFamilyCreationGate(ITenantSettingsRepository settings) : IProcedureFamilyCreationGate
{
    public async Task<bool> IsFamilyBlockedAsync(
        Guid tenantId,
        string? procedureFamily,
        CancellationToken ct = default)
    {
        var current = await settings.GetAsync(tenantId, ct).ConfigureAwait(false);
        // Sin fila de settings: MATRICULAS se considera bloqueada (default AllowInitial=false);
        // TRASPASO/OTROS no bloqueados (default false).
        var effective = current ?? TenantSettings.Default(tenantId);
        return effective.IsProcedureFamilyBlocked(procedureFamily);
    }
}
