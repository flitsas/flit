using Flit.Infrastructure.Notifications.Theme;
using Flit.Tramites.Application.Identity;

namespace Flit.Infrastructure.Notifications.Identity;

/// <summary>
/// <see cref="IManualCaptureLinkUrlBuilder"/> (HU #14305): la MISMA URL que el correo al titular (base pública de
/// <c>PublicBranding:PublicBaseUrl</c> + ruta de captura), delegando en <see cref="ManualCaptureEmailComposer.BuildLink"/>.
/// </summary>
internal sealed class ManualCaptureLinkUrlBuilder(EmailThemePublicBrandingOptions options) : IManualCaptureLinkUrlBuilder
{
    public string Build(string token) => ManualCaptureEmailComposer.BuildLink(options.PublicBaseUrl, token);
}
