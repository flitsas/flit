using Flit.Admin.Domain.Companies.Settings;
using Flit.Tramites.Application.UseCases.Avaluos;

namespace Flit.Infrastructure.Consultations.Avaluos;

/// <summary>
/// Puente (Feature #10707) que lee los proveedores de avalúo habilitados del tenant desde
/// <c>admin.tenant_operational_policies</c> (vía <see cref="ITenantSettingsRepository"/>) y los proyecta
/// al <see cref="AvaluoEnabledSet"/> que consume el handler de sugerencia. Vive en Infraestructura
/// porque cruza los límites Admin↔Trámites. Sin fila para el tenant ⇒ default (solo Fasecolda).
/// </summary>
internal sealed class TenantAvaluoPolicyProvider(ITenantSettingsRepository repository)
    : IAvaluoProviderPolicy
{
    public async Task<AvaluoEnabledSet> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        var settings = await repository.GetAsync(tenantId, ct).ConfigureAwait(false);
        if (settings is null)
            return AvaluoEnabledSet.Default;

        var config = settings.AvaluoProviderConfig;
        return new AvaluoEnabledSet(config.Enabled, config.Primary);
    }
}
