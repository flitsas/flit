using Flit.Infrastructure.Notifications.Identity;
using Flit.Modules.Security.Domain.Auth;

namespace Flit.Infrastructure.Notifications.Preview;

/// <summary>
/// Muestra de <c>identidad.captura-manual-rechazo</c> para el banco de pruebas de notificaciones (HU #13299, Épica #13202). Compone
/// con el MISMO <see cref="ManualCaptureEmailComposer.ComposeRejected"/> que el envío real, con datos sintéticos: nunca un titular,
/// un token ni un motivo libre reales.
/// </summary>
public static class ManualCaptureRejectedEmailPreviewSample
{
    public const string SampleRecipientName = ManualCaptureEmailPreviewSample.SampleRecipientName;

    /// <summary>Etiqueta de ejemplo de la lista cerrada de motivos de rechazo («Imagen borrosa»).</summary>
    public const string SampleReasonLabel = "Imagen borrosa";

    /// <summary>Enlace de ejemplo (dominio reservado <c>.example</c>): no lleva a ninguna captura real.</summary>
    public const string SampleLink = "https://app.flit.example/verificacion/TOKEN-NUEVO-DE-EJEMPLO";

    public static (string Subject, string Html) Build(string? assetsBaseUrl = null, EmailTheme? theme = null) =>
        ManualCaptureEmailComposer.ComposeRejected(
            SampleRecipientName,
            SampleReasonLabel,
            SampleLink,
            theme ?? EmailTheme.Flit,
            string.IsNullOrWhiteSpace(assetsBaseUrl) ? NotificationEmailAssetsOptions.LocalFallbackBaseUrl : assetsBaseUrl);
}
