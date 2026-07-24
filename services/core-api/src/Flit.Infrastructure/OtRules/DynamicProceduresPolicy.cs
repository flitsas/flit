using Flit.Admin.Domain.OtProfile;
using Flit.Tramites.Application.UseCases.ProcedureInstances;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Binding del feature flag <c>F08_DynamicProcedures</c> (FEATURE-08 / HU-BE-06) sobre
/// <c>admin.ot_feature_flags</c> (por tenant). Si el tenant no tiene la fila del flag, se considera
/// deshabilitado (→ wizard estático). Desacopla Trámites del módulo Admin: el puerto vive en
/// Application, el binding en Infraestructura.
/// </summary>
internal sealed class DynamicProceduresPolicy(IOtFeatureFlagRepository flags) : IDynamicProceduresPolicy
{
    private const string FlagKey = "F08_DynamicProcedures";

    public async Task<bool> IsEnabledAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenantFlags = await flags.ListByTenantAsync(tenantId, ct);
        var flag = tenantFlags.FirstOrDefault(f =>
            string.Equals(f.FlagKey, FlagKey, StringComparison.OrdinalIgnoreCase));
        return flag?.IsEnabled ?? false;
    }
}
