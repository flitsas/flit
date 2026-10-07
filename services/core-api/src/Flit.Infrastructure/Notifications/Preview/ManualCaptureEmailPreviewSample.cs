using Flit.Infrastructure.Notifications.Identity;
using Flit.Modules.Security.Domain.Auth;

namespace Flit.Infrastructure.Notifications.Preview;

/// <summary>
/// Muestra de <c>identidad.captura-manual</c> para el banco de pruebas de notificaciones (HU #13287, Épica #13202). Compone con
/// el MISMO <see cref="ManualCaptureEmailComposer"/> que el envío real, con datos sintéticos: nunca un titular ni un token reales.
/// </summary>
public static class ManualCaptureEmailPreviewSample
{
    public const string SampleRecipientName = "Ana María Pérez Gómez";

    /// <summary>Enlace de ejemplo (dominio reservado <c>.example</c>): no lleva a ninguna captura real.</summary>
    public const string SampleLink = "https://app.flit.example/captura-manual/TOKEN-DE-EJEMPLO";

    public static (string Subject, string Html) Build(string? assetsBaseUrl = null, EmailTheme? theme = null) =>
        ManualCaptureEmailComposer.Compose(
            SampleRecipientName,
            SampleLink,
            theme ?? EmailTheme.Flit,
            string.IsNullOrWhiteSpace(assetsBaseUrl) ? NotificationEmailAssetsOptions.LocalFallbackBaseUrl : assetsBaseUrl);
}
