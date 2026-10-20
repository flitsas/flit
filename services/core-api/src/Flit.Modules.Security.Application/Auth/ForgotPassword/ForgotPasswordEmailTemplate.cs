using Flit.Modules.Security.Application.Auth;

namespace Flit.Modules.Security.Application.Auth.ForgotPassword;

/// <summary>
/// Plantilla del correo de recuperación de contraseña (HU #10169), extraída de
/// <see cref="ForgotPasswordHandler"/> como función de composición pura (HU #11351).
/// </summary>
public static class ForgotPasswordEmailTemplate
{
    /// <summary>Asunto con el tema FLIT. Con tema de marca ver <see cref="BuildSubject"/>.</summary>
    public const string Subject = "Recuperación de contraseña — FLIT";

    /// <summary>Asunto firmado por la marca con tema de marca; exactamente <see cref="Subject"/> con FLIT.</summary>
    public static string BuildSubject(Flit.Modules.Security.Domain.Auth.EmailTheme? theme = null) =>
        $"Recuperación de contraseña — {FlitBrandedEmailLayout.PlatformName(theme)}";

    public static string BuildResetLink(string resetUrlBase, string rawToken)
    {
        var separator = resetUrlBase.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{resetUrlBase}{separator}token={Uri.EscapeDataString(rawToken)}";
    }

    /// <summary>
    /// HU #11351 — composición pura: asunto y cuerpo a partir únicamente de sus argumentos
    /// (<paramref name="link"/> ya calculado con <see cref="BuildResetLink"/>). Sin E/S, sin
    /// reloj, sin aleatoriedad, sin estado.
    /// </summary>
    public static ComposedEmail Compose(
        string displayName,
        string link,
        int lifetimeMinutes,
        string? assetsBaseUrl = null,
        Flit.Modules.Security.Domain.Auth.EmailTheme? theme = null) =>
        new(BuildSubject(theme), BuildHtmlBody(displayName, link, lifetimeMinutes, assetsBaseUrl, theme));

    private static string BuildHtmlBody(
        string displayName,
        string link,
        int lifetimeMinutes,
        string? assetsBaseUrl,
        Flit.Modules.Security.Domain.Auth.EmailTheme? theme = null)
    {
        var greetingName = string.IsNullOrWhiteSpace(displayName) ? "usuario" : displayName;
        var name = System.Net.WebUtility.HtmlEncode(greetingName);
        var body =
            FlitBrandedEmailLayout.ParagraphHtml($"Hola {name},")
            + FlitBrandedEmailLayout.Paragraph(
                $"Recibimos una solicitud para restablecer tu contraseña en {FlitBrandedEmailLayout.PlatformName(theme)}. Haz clic en el siguiente enlace para crear una nueva contraseña:")
            + FlitBrandedEmailLayout.ActionLink(link, "Restablecer mi contraseña", FlitBrandedEmailLayout.BrandLinkColor(theme))
            + FlitBrandedEmailLayout.Paragraph(
                $"El enlace caduca en {lifetimeMinutes} minutos. Si no solicitaste este cambio, puedes ignorar este mensaje; tu contraseña seguirá siendo la misma.");

        return FlitBrandedEmailLayout.Wrap(
            headline: "¡RECUPERACIÓN DE CONTRASEÑA!",
            bodyInnerHtml: body,
            closingHeadline: null,
            assetsBaseUrl: assetsBaseUrl,
            theme: theme);
    }
}
