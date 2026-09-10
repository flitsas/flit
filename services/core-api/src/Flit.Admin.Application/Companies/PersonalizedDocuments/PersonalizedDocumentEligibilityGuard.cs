using Flit.Admin.Domain.Companies.Settings;

namespace Flit.Admin.Application.Companies.PersonalizedDocuments;

/// <summary>
/// Fuente ÚNICA de verdad de la elegibilidad de la funcionalidad (HU #11313 §8 DT-7 del plan
/// técnico, desacoplada del canal por HU #11357/#11362 y ADR-0043): las rutas de escritura de
/// documentos personalizados solo operan si el tenant tiene el interruptor propio
/// <see cref="TenantSettings.PersonalizedDocumentsEnabled"/> encendido, leído SIEMPRE de
/// <c>admin.tenant_operational_policies.personalized_documents_enabled</c> — nunca de
/// <c>notification_channel</c> (ADR-0043 sustituye la fuente que usaba esta clase hasta la HU #11362;
/// antes de esa HU, <c>notification_channel = 'tenant_api'</c> era, de facto, este interruptor).
/// </summary>
/// <remarks>
/// Renombrada desde <c>PersonalizedDocumentChannelGuard</c> (ADR-0043, §Notas para agentes — Backend
/// Agent): el nombre anterior mencionaba el canal, que ya no gobierna esta capacidad. La superficie
/// NO cambia: mismos cuatro handlers de escritura (Create/Confirm/Activate/Deactivate), mismo método
/// <see cref="IsWriteEnabledAsync"/>, misma firma; el listado <c>GET</c> sigue sin aplicarla a propósito.
/// </remarks>
public static class PersonalizedDocumentEligibilityGuard
{
    /// <summary>
    /// <c>true</c> si las rutas de escritura pueden operar (interruptor propio encendido). Un tenant
    /// sin política configurada cae al default (<c>false</c>) ⇒ deshabilitado.
    /// </summary>
    public static async Task<bool> IsWriteEnabledAsync(
        ITenantSettingsRepository settingsRepository,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settingsRepository);

        var settings = await settingsRepository.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return settings?.PersonalizedDocumentsEnabled ?? false;
    }
}
