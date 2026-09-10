using Flit.Admin.Domain.Companies.Settings;

namespace Flit.Admin.Application.Companies.Settings.GetActiveModules;

/// <summary>
/// Caso de uso de lectura de los 3 flags de módulos activos del dashboard (HU #12251,
/// Feature #12249). A diferencia de
/// <see cref="Flit.Admin.Application.Companies.Settings.GetTenantSettings.GetTenantSettingsHandler"/>,
/// NUNCA devuelve null: un tenant sin fila en <c>admin.tenant_operational_policies</c> resuelve
/// a <see cref="TenantSettings.Default"/> (AC3) — mismo patrón "get-or-default" que
/// <c>ProcedureFamilyCreationGate</c>/<c>UpdateTenantSettingsHandler</c>. Reutiliza el MISMO
/// <see cref="ITenantSettingsRepository"/> que el resto de consumidores de
/// <c>TenantSettings</c>: no duplica la lógica de "sin fila → defaults".
/// </summary>
public sealed class GetActiveModulesHandler
{
    private readonly ITenantSettingsRepository _repository;

    public GetActiveModulesHandler(ITenantSettingsRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<ActiveModulesResponse> HandleAsync(
        GetActiveModulesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var settings = await _repository.GetAsync(query.TenantId, cancellationToken).ConfigureAwait(false)
            ?? TenantSettings.Default(query.TenantId);

        return new ActiveModulesResponse(
            settings.TramitesModuleEnabled,
            settings.ComparendosModuleEnabled,
            settings.ResolucionesModuleEnabled);
    }
}
