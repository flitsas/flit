namespace Flit.Modules.Security.Application.Auth;

/// <summary>
/// Plantilla de bienvenida tras registro (banco de pruebas / canal FLIT).
/// Enlace de acceso: login principal de la plataforma.
/// </summary>
public static class WelcomeRegistrationEmailTemplate
{
    public const string Subject = "¡Gracias por registrarte! — FLIT";

    /// <summary>Login principal FLIT (ruta <c>/login</c> del frontend).</summary>
    public const string DefaultLoginUrl = "https://dev.flitsas.online/login";

    /// <summary>
    /// Composición pura: misma entrada → misma salida. Sin E/S ni estado.
    /// </summary>
    public static ComposedEmail Compose(string? loginUrl = null, string? assetsBaseUrl = null)
    {
        var url = string.IsNullOrWhiteSpace(loginUrl) ? DefaultLoginUrl : loginUrl.Trim();
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
            assetsBaseUrl: assetsBaseUrl);

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
