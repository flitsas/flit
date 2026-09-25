using Flit.Admin.Domain.Companies.Settings;

namespace Flit.Admin.Application.Companies.Settings.GetTenantSettings;

/// <summary>
/// Caso de uso de lectura de la configuración operativa del tenant (AC3).
/// Devuelve <c>null</c> cuando no existe fila en
/// <c>admin.tenant_operational_policies</c> (el endpoint traduce a 404).
/// </summary>
public sealed class GetTenantSettingsHandler
{
    private readonly ITenantSettingsRepository _repository;
    private readonly ITenantProductFlags _products;

    public GetTenantSettingsHandler(ITenantSettingsRepository repository, ITenantProductFlags products)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _products = products ?? throw new ArgumentNullException(nameof(products));
    }

    public async Task<TenantSettingsResponse?> HandleAsync(
        GetTenantSettingsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var settings = await _repository.GetAsync(query.TenantId, cancellationToken).ConfigureAwait(false);

        if (settings is null)
            return null;

        // HU #12967 (B-07): Trámites y Comparendos salen de platform.tenant_products.
        var products = await _products.GetAsync(query.TenantId, cancellationToken).ConfigureAwait(false);
        return SettingsMapper.ToResponse(settings) with
        {
            TramitesModuleEnabled = products.Tramites,
            ComparendosModuleEnabled = products.Comparendos,
        };
    }
}
