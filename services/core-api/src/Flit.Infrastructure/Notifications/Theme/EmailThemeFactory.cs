using Flit.Admin.Domain.Companies.Branding;
using Flit.Modules.Security.Domain.Auth;

namespace Flit.Infrastructure.Notifications.Theme;

/// <summary>
/// Construye un <see cref="EmailTheme"/> a partir de un snapshot de marca YA resuelto (publicado o
/// borrador) — HU #12428 AC1/AC2/AC3, HU #12431 (borrador de la propia cabeza). Único punto que
/// combina <see cref="BrandingDraft"/> con <c>PublicBranding:PublicBaseUrl</c> para producir la URL
/// ABSOLUTA y versionada del logotipo (AC3): nunca se incrusta el dato, nunca se usa una dirección
/// prefirmada.
/// </summary>
public static class EmailThemeFactory
{
    /// <summary>Snapshot publicado de una cabeza MARCA_BLANCA activa (propia o heredada) ⇒ tema
    /// <c>Brand</c>. Campos faltantes del snapshot (nunca deberían faltar en un publicado válido, pero
    /// el borrador comparte el mismo record) se completan con los valores de <see cref="EmailTheme.Flit"/>.</summary>
    public static EmailTheme FromPublished(BrandingDraft published, int publishedVersion, string publicBaseUrl) =>
        Build(published, publishedVersion, publicBaseUrl, EmailThemeKind.Brand);

    /// <summary>
    /// HU #12431 — borrador SIN publicar de la propia cabeza (<c>source=draft</c> en
    /// <c>GET /company/branding/email-sample</c>). No se persiste nada: es una vista previa. Se marca
    /// igual con <see cref="EmailThemeKind.Brand"/> — el llamador (endpoint) decide si etiqueta la
    /// respuesta como <c>"draft-partial"</c> cuando el borrador está incompleto; el dominio del tema
    /// no conoce ese matiz de presentación.
    /// </summary>
    public static EmailTheme FromDraft(BrandingDraft draft, string publicBaseUrl) =>
        Build(draft, publishedVersion: 0, publicBaseUrl, EmailThemeKind.Brand);

    private static EmailTheme Build(
        BrandingDraft snapshot, int publishedVersion, string publicBaseUrl, EmailThemeKind kind)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var platformName = string.IsNullOrWhiteSpace(snapshot.PlatformName)
            ? EmailTheme.Flit.PlatformName
            : snapshot.PlatformName;

        var colors = snapshot.Colors;
        var primary = string.IsNullOrWhiteSpace(colors?.Primary) ? EmailTheme.Flit.Primary : colors!.Primary;
        var secondary = string.IsNullOrWhiteSpace(colors?.Secondary) ? EmailTheme.Flit.Secondary : colors!.Secondary;
        var onPrimary = string.IsNullOrWhiteSpace(colors?.OnPrimary) ? EmailTheme.Flit.OnPrimary : colors!.OnPrimary;

        var logoUrl = snapshot.LogoId is { } logoId
            ? CombineAbsoluteUrl(publicBaseUrl, $"/api/v1/public/branding/logos/{logoId}")
            : null;

        return new EmailTheme(kind, platformName, logoUrl, primary, secondary, onPrimary, publishedVersion);
    }

    private static string CombineAbsoluteUrl(string baseUrl, string path)
    {
        var trimmedBase = string.IsNullOrWhiteSpace(baseUrl) ? string.Empty : baseUrl.Trim().TrimEnd('/');
        return trimmedBase + path;
    }
}
