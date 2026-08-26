using Flit.Modules.Security.Application.Auth;

namespace Flit.Modules.Security.Application.Auth.AdminResetPassword;

/// <summary>
/// Plantilla del correo de reset administrativo de contraseña (HU #10170), extraída de
/// <see cref="AdminResetPasswordHandler"/> como función de composición pura (HU #11351).
/// </summary>
/// <remarks>
/// Riesgo señalado en el refinamiento: esta clase NUNCA genera la contraseña temporal — la
/// recibe como argumento ya generada por <c>ITemporaryPasswordGenerator</c> en el handler. Si la
/// generación se arrastrara aquí dentro, un fallo de composición dejaría al usuario con el hash
/// ya actualizado y sin acceso.
/// </remarks>
public static class AdminResetPasswordEmailTemplate
{
    public const string Subject = "Tu contraseña fue restablecida — FLIT";

    /// <summary>
    /// HU #11351 — composición pura: asunto y cuerpo a partir únicamente de
    /// <paramref name="displayName"/> y <paramref name="temporaryPassword"/> (ya generada por el
    /// llamante). Sin E/S, sin reloj, sin aleatoriedad, sin estado — la misma entrada produce
    /// siempre la misma salida.
    /// </summary>
    public static ComposedEmail Compose(
        string displayName, string temporaryPassword, string? assetsBaseUrl = null) =>
        new(Subject, BuildBody(displayName, temporaryPassword, assetsBaseUrl));

    private static string BuildBody(
        string displayName, string temporaryPassword, string? assetsBaseUrl)
    {
        var nameRaw = string.IsNullOrWhiteSpace(displayName) ? "usuario" : displayName;
        var name = System.Net.WebUtility.HtmlEncode(nameRaw);
        var password = System.Net.WebUtility.HtmlEncode(temporaryPassword);
        var body =
            FlitBrandedEmailLayout.ParagraphHtml($"Hola {name},")
            + FlitBrandedEmailLayout.Paragraph(
                "Un administrador restableció tu contraseña en FLIT. Tu contraseña temporal es:")
            + FlitBrandedEmailLayout.ParagraphHtml(
                $"<strong style=\"font-size:18px;letter-spacing:0.04em;\">{password}</strong>")
            + FlitBrandedEmailLayout.Paragraph(
                "Por seguridad, deberás definir una nueva contraseña la próxima vez que inicies sesión.");

        return FlitBrandedEmailLayout.Wrap(
            headline: "¡CONTRASEÑA RESTABLECIDA!",
            bodyInnerHtml: body,
            closingHeadline: null,
            assetsBaseUrl: assetsBaseUrl);
    }
}
