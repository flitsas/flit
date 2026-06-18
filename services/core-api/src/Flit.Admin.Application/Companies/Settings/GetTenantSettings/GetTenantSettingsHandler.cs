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

    public GetTenantSettingsHandler(ITenantSettingsRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<TenantSettingsResponse?> HandleAsync(
        GetTenantSettingsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var settings = await _repository.GetAsync(query.TenantId, cancellationToken).ConfigureAwait(false);

        return settings is null ? null : SettingsMapper.ToResponse(settings);
    }
}
