using System.Globalization;
using System.Net;
using System.Text;
using Flit.Modules.Security.Domain.Auth;

namespace Flit.Modules.Security.Application.Auth;

/// <summary>
/// Envoltorio de las plantillas Security: con tema FLIT delega en el layout único
/// <see cref="FlitEmailLayout"/> (Correos 2.0); con tema de marca, en <see cref="BrandedEmailChrome"/>.
/// </summary>
/// <remarks>
/// Los composers de cada plantilla aportan el título y el HTML interno (enunciados y enlaces);
/// este layout solo aplica la marca. Assets públicos: la base que recibe <c>Wrap</c> (en producción,
/// <c>Notifications:EmailAssets:BaseUrl</c> vía <see cref="SecurityEmailAssetsOptions"/>); sin base,
/// <see cref="DefaultAssetsBaseUrl"/>.
/// </remarks>
public static class FlitBrandedEmailLayout
{
    /// <summary>
    /// Bug #13194 — respaldo SOLO para desarrollo local (frontend en <c>localhost:3000</c>). Los
    /// ambientes desplegados definen <c>Notifications__EmailAssets__BaseUrl</c>; nunca se cae a DEV.
    /// </summary>
    public const string DefaultAssetsBaseUrl = FlitEmailLayout.DefaultAssetsBaseUrl;
    public const string DefaultHeaderFileName = FlitEmailLayout.BannerFileName;
    public const string DefaultLogoFileName = FlitEmailLayout.LogoFileName;
    public const string PrivacyPolicyUrl = Flit.Modules.Security.Domain.Auth.BrandedEmailChrome.FlitPrivacyPolicyUrl;
    public const string SupportEmail = Flit.Modules.Security.Domain.Auth.BrandedEmailChrome.FlitSupportEmail;

    private const string LinkBlue = "#2F6FED";

    public static string ResolveHeaderUrl(string assetsBaseUrl) => FlitEmailLayout.BannerUrl(assetsBaseUrl);

    public static string ResolveLogoUrl(string assetsBaseUrl) => FlitEmailLayout.LogoUrl(assetsBaseUrl);

    /// <summary>
    /// Envuelve el cuerpo de una plantilla Security en el formato FLIT de marca.
    /// </summary>
    /// <param name="headline">Título principal (ya sin HTML).</param>
    /// <param name="bodyInnerHtml">Fragmentos HTML del cuerpo (párrafos/enlaces ya escapados).</param>
    /// <param name="closingHeadline">Título de cierre opcional (sin HTML).</param>
    /// <param name="assetsBaseUrl">Base HTTPS de assets; si es null/vacío usa el default público.</param>
    /// <param name="theme">
    /// HU #12428 AC1/AC2/AC8/AC9 — tema de correo ya resuelto. <c>null</c> o
    /// <see cref="Flit.Modules.Security.Domain.Auth.EmailThemeKind.Flit"/> ⇒ este método NO cambia
    /// una sola línea de su salida (AC9, paridad byte a byte): el parámetro se agrega al final,
    /// opcional, precisamente para que ningún llamador existente tenga que cambiar. Con
    /// <see cref="Flit.Modules.Security.Domain.Auth.EmailThemeKind.Brand"/> delega en
    /// <see cref="Flit.Modules.Security.Domain.Auth.BrandedEmailChrome"/> (AC2: logo/nombre en
    /// encabezado, color principal en el título, nombre en el pie) reutilizando EXACTAMENTE el mismo
    /// <paramref name="bodyInnerHtml"/> que hoy compone cada plantilla (AC2, "cuerpo funcional...
    /// idénticos").
    /// </param>
    public static string Wrap(
        string headline,
        string bodyInnerHtml,
        string? closingHeadline = null,
        string? assetsBaseUrl = null,
        Flit.Modules.Security.Domain.Auth.EmailTheme? theme = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headline);
        ArgumentNullException.ThrowIfNull(bodyInnerHtml);

        if (theme is { IsBrand: true })
        {
            return Flit.Modules.Security.Domain.Auth.BrandedEmailChrome.Wrap(
                theme, headline, bodyInnerHtml, closingHeadline);
        }

        // Correos 2.0 — tema FLIT sobre el layout único (banner, título, cuerpo centrado, pie).
        var cuerpo = new StringBuilder(bodyInnerHtml.Length + 256);
        cuerpo.Append("<div style=\"text-align:center;\">").Append(bodyInnerHtml).Append("</div>");
        if (!string.IsNullOrWhiteSpace(closingHeadline))
        {
            cuerpo.Append(CultureInfo.InvariantCulture,
                $"<p style=\"margin:8px 0 0;text-align:center;font-size:18px;font-weight:700;color:{FlitEmailLayout.Title};\">{Enc(closingHeadline)}</p>");
        }

        return FlitEmailLayout.Documento(
            assetsBaseUrl,
            FlitEmailLayout.Titulo(headline),
            cuerpo.ToString(),
            FlitEmailLayout.PieFlit(assetsBaseUrl, FlitEmailLayout.SoporteEstandarHtml, politicaPrivacidad: true));
    }

    /// <summary>
    /// Nombre de la plataforma que firma el correo: el de la marca con tema <c>Brand</c> (y nombre
    /// informado); <c>"FLIT"</c> en cualquier otro caso, para que la salida FLIT no cambie.
    /// </summary>
    public static string PlatformName(Flit.Modules.Security.Domain.Auth.EmailTheme? theme) =>
        theme is { IsBrand: true } && !string.IsNullOrWhiteSpace(theme.PlatformName)
            ? theme.PlatformName.Trim()
            : "FLIT";

    /// <summary>Color del enlace de acción: el principal de la marca con tema <c>Brand</c>; <c>null</c> (azul FLIT) si no.</summary>
    public static string? BrandLinkColor(Flit.Modules.Security.Domain.Auth.EmailTheme? theme) =>
        theme is { IsBrand: true } ? Flit.Modules.Security.Domain.Auth.BrandedEmailChrome.LinkColor(theme) : null;

    public static string Paragraph(string text) =>
        $"<p style=\"margin:0 0 12px;\">{Enc(text)}</p>";

    public static string ParagraphHtml(string innerHtml) =>
        $"<p style=\"margin:0 0 12px;\">{innerHtml}</p>";

    public static string ActionLink(string href, string label, string? linkColor = null) =>
        $"<p style=\"margin:0 0 16px;\"><a href=\"{EncAttr(href)}\" style=\"color:{linkColor ?? LinkBlue};font-size:15px;text-decoration:underline;word-break:break-all;\">{Enc(label)}</a></p>";

    /// <summary>
    /// Saludo «Hola {nombre},». Con tema de marca, EXACTAMENTE el párrafo de siempre (golden de
    /// marca intacto); con FLIT, el nombre destacado en azul como en la maqueta 2.0.
    /// </summary>
    public static string Greeting(string nameHtml, Flit.Modules.Security.Domain.Auth.EmailTheme? theme) =>
        theme is { IsBrand: true }
            ? ParagraphHtml($"Hola {nameHtml},")
            : ParagraphHtml($"Hola {FlitEmailLayout.Destacado(nameHtml)},");

    /// <summary>
    /// Acción principal. Con tema de marca, el enlace subrayado de siempre (golden de marca intacto);
    /// con FLIT, el botón HTML/CSS del layout 2.0. El texto es el mismo en ambos.
    /// </summary>
    public static string Action(string href, string label, Flit.Modules.Security.Domain.Auth.EmailTheme? theme) =>
        theme is { IsBrand: true }
            ? ActionLink(href, label, BrandLinkColor(theme))
            : FlitEmailLayout.Boton(label, href);

    /// <summary>
    /// Enlace cuyo texto es la propia URL (bienvenida). Con marca, el de siempre; con FLIT, enlace
    /// con el acento 2.0 — no es un botón porque su texto no es una acción.
    /// </summary>
    public static string UrlLink(string href, string label, Flit.Modules.Security.Domain.Auth.EmailTheme? theme) =>
        theme is { IsBrand: true }
            ? ActionLink(href, label, BrandLinkColor(theme))
            : $"<p style=\"margin:0 0 16px;\">{FlitEmailLayout.Enlace(href, label)}</p>";

    private static string Enc(string value) => WebUtility.HtmlEncode(value);

    private static string EncAttr(string value) => WebUtility.HtmlEncode(value);
}
