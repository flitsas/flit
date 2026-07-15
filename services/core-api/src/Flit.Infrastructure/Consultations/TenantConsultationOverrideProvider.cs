using Flit.Admin.Domain.Companies.Settings;
using Flit.Tramites.Application.UseCases.Consultations;

namespace Flit.Infrastructure.Consultations;

/// <summary>
/// Puente (HU #10478, Fase 5) que lee la configuración de consultas del tenant desde
/// <c>admin.tenant_operational_policies</c> (vía <see cref="ITenantSettingsRepository"/>) y la proyecta
/// al <see cref="ConsultationTenantOverride"/> del chain resolver. Vive en Infraestructura porque cruza
/// los límites Admin↔Trámites. Sin fila para el tenant ⇒ <c>null</c> (defaults globales). Config vacía
/// ⇒ solo se propaga el <c>runt_failover_timeout_ms</c>; las cadenas caen a los defaults globales.
/// </summary>
internal sealed class TenantConsultationOverrideProvider(ITenantSettingsRepository repository)
    : IConsultationTenantOverrideProvider
{
    public async Task<ConsultationTenantOverride?> GetAsync(Guid tenantId, CancellationToken ct)
    {
        var settings = await repository.GetAsync(tenantId, ct).ConfigureAwait(false);
        if (settings is null)
            return null;

        Dictionary<string, ConsultationChainSelection>? chains = null;
        if (!settings.ConsultationProviderConfig.IsEmpty)
        {
            chains = new Dictionary<string, ConsultationChainSelection>(StringComparer.OrdinalIgnoreCase);
            foreach (var (kind, selection) in settings.ConsultationProviderConfig.ByKind)
            {
                chains[kind] = new ConsultationChainSelection(selection.Primary, selection.Fallback);
            }
        }

        return new ConsultationTenantOverride(chains, settings.RuntFailoverTimeoutMs, settings.OnlyOwnVehicles);
    }
}
