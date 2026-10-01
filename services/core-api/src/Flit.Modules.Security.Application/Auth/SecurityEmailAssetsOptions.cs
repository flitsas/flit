namespace Flit.Modules.Security.Application.Auth;

/// <summary>
/// Bug #13194 — base pública de los assets (banner/logo) de los correos del módulo Security
/// (invitación, recuperación, reset administrativo y bienvenida). Se enlaza contra la MISMA sección
/// que <c>Flit.Infrastructure.Notifications.NotificationEmailAssetsOptions</c>
/// (<c>Notifications:EmailAssets:BaseUrl</c>), sin que Application dependa de Infrastructure: no es
/// una clave nueva, es la que el despliegue ya inyecta por ambiente.
/// </summary>
public sealed class SecurityEmailAssetsOptions
{
    public const string SectionName = "Notifications:EmailAssets";

    /// <summary>
    /// URL absoluta de la carpeta <c>email-assets</c> del frontend del ambiente. Vacío ⇒ el layout
    /// usa <see cref="FlitBrandedEmailLayout.DefaultAssetsBaseUrl"/> (solo desarrollo local).
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;
}
