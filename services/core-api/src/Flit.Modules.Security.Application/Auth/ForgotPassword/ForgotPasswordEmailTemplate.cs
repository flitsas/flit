using Flit.Modules.Security.Application.Auth;

namespace Flit.Modules.Security.Application.Auth.ForgotPassword;

/// <summary>
/// Plantilla del correo de recuperación de contraseña (HU #10169), extraída de
/// <see cref="ForgotPasswordHandler"/> como función de composición pura (HU #11351).
/// </summary>
public static class ForgotPasswordEmailTemplate
{
    public const string Subject = "Recuperación de contraseña — FLIT";

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
        string displayName, string link, int lifetimeMinutes, string? assetsBaseUrl = null) =>
        new(Subject, BuildHtmlBody(displayName, link, lifetimeMinutes, assetsBaseUrl));

    private static string BuildHtmlBody(
        string displayName, string link, int lifetimeMinutes, string? assetsBaseUrl)
    {
        var greetingName = string.IsNullOrWhiteSpace(displayName) ? "usuario" : displayName;
        var name = System.Net.WebUtility.HtmlEncode(greetingName);
        var body =
            FlitBrandedEmailLayout.ParagraphHtml($"Hola {name},")
            + FlitBrandedEmailLayout.Paragraph(
                "Recibimos una solicitud para restablecer tu contraseña en FLIT. Haz clic en el siguiente enlace para crear una nueva contraseña:")
            + FlitBrandedEmailLayout.ActionLink(link, "Restablecer mi contraseña")
            + FlitBrandedEmailLayout.Paragraph(
                $"El enlace caduca en {lifetimeMinutes} minutos. Si no solicitaste este cambio, puedes ignorar este mensaje; tu contraseña seguirá siendo la misma.");

        return FlitBrandedEmailLayout.Wrap(
            headline: "¡RECUPERACIÓN DE CONTRASEÑA!",
            bodyInnerHtml: body,
            closingHeadline: null,
            assetsBaseUrl: assetsBaseUrl);
    }
}
