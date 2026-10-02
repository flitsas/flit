namespace Flit.Modules.Security.Application.Auth;

/// <summary>
/// Plantilla de bienvenida tras registro (banco de pruebas / canal FLIT).
/// Enlace de acceso: login de la plataforma del ambiente (Bug #13194: nunca un literal; lo arma
/// <see cref="BuildLoginUrl"/> desde la configuración y lo resuelve el handler por tenant).
/// </summary>
public static class WelcomeRegistrationEmailTemplate
{
    public const string Subject = "¡Gracias por registrarte! — FLIT";

    /// <summary>Ruta del login principal en el frontend.</summary>
    public const string LoginPath = "/login";

    /// <summary>
    /// Bug #13194 — URL de login del ambiente a partir de una URL configurada del MISMO frontend
    /// (<c>Invitations:ActivateUrlBase</c>): se conserva esquema + host (+ puerto) y se reemplaza el
    /// path por <see cref="LoginPath"/>.
    /// <para>Una base relativa o inválida es un error de CONFIGURACIÓN y falla rápido con
    /// <see cref="InvalidOperationException"/>: devolver solo <c>/login</c> dejaba un enlace muerto en
    /// el correo sin que nadie se enterara.</para>
    /// </summary>
    public static string BuildLoginUrl(string configuredFrontendUrl)
    {
        ArgumentNullException.ThrowIfNull(configuredFrontendUrl);
        if (!Uri.TryCreate(configuredFrontendUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException(
                "Invitations:ActivateUrlBase debe ser una URL absoluta http(s) del frontend del ambiente; "
                + "con una base relativa o inválida el correo no puede llevar el enlace de inicio de sesión.");
        }

        return uri.GetLeftPart(UriPartial.Authority) + LoginPath;
    }

    /// <summary>
    /// Composición pura: misma entrada → misma salida. Sin E/S ni estado.
    /// </summary>
    public static ComposedEmail Compose(
        string loginUrl,
        string? assetsBaseUrl = null,
        Flit.Modules.Security.Domain.Auth.EmailTheme? theme = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loginUrl);
        var url = loginUrl.Trim();
        var display = ToDisplayUrl(url);
        var body =
            FlitBrandedEmailLayout.Paragraph("Nos alegra que estés aquí.")
            + FlitBrandedEmailLayout.Paragraph(
                "Para ingresar a tu cuenta haz clic en el siguiente enlace:")
            + FlitBrandedEmailLayout.ActionLink(url, display);

        var html = FlitBrandedEmailLayout.Wrap(
            headline: "¡GRACIAS POR REGISTRARTE!",
            bodyInnerHtml: body,
            closingHeadline: "¡Disfruta de todos tus beneficios!",
            assetsBaseUrl: assetsBaseUrl,
            theme: theme);

        return new ComposedEmail(Subject, html);
    }

    /// <summary>Texto visible del enlace sin esquema (como en el diseño de marca).</summary>
    public static string ToDisplayUrl(string absoluteUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteUrl);
        var trimmed = absoluteUrl.Trim();
        if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return trimmed["https://".Length..];
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return trimmed["http://".Length..];
        return trimmed;
    }
}
