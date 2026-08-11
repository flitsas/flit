using Flit.Admin.Domain.Companies.Settings;

namespace Flit.Admin.Application.Companies.PersonalizedDocuments;

/// <summary>
/// Fuente ÚNICA de verdad del interruptor de la funcionalidad (HU #11313, §8 DT-7 del plan técnico):
/// las rutas de escritura de documentos personalizados solo operan si el canal del tenant es
/// <see cref="NotificationChannel.TenantApi"/>, leído SIEMPRE de
/// <c>admin.tenant_operational_policies.notification_channel</c> — nunca de un booleano paralelo ni de
/// una lista fija en código.
/// </summary>
public static class PersonalizedDocumentChannelGuard
{
    /// <summary>
    /// <c>true</c> si las rutas de escritura pueden operar (canal <c>TENANT_API</c>). Un tenant sin
    /// política configurada cae al default (<see cref="NotificationChannel.FlitSmtp"/>) ⇒ deshabilitado.
    /// </summary>
    public static async Task<bool> IsWriteEnabledAsync(
        ITenantSettingsRepository settingsRepository,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settingsRepository);

        var settings = await settingsRepository.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return settings?.NotificationChannel == NotificationChannel.TenantApi;
    }
}
