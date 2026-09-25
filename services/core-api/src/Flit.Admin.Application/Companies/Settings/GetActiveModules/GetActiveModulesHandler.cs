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
    private readonly ITenantProductFlags _products;

    public GetActiveModulesHandler(ITenantSettingsRepository repository, ITenantProductFlags products)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _products = products ?? throw new ArgumentNullException(nameof(products));
    }

    public async Task<ActiveModulesResponse> HandleAsync(
        GetActiveModulesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var settings = await _repository.GetAsync(query.TenantId, cancellationToken).ConfigureAwait(false)
            ?? TenantSettings.Default(query.TenantId);

        // HU #12967 (B-07): Trámites y Comparendos salen de platform.tenant_products; Resoluciones sigue
        // en la política porque está fuera de la suite v1.
        var products = await _products.GetAsync(query.TenantId, cancellationToken).ConfigureAwait(false);
        return new ActiveModulesResponse(
            products.Tramites,
            products.Comparendos,
            settings.ResolucionesModuleEnabled);
    }
}
