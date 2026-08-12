namespace Flit.Modules.Security.Application.Auth;

/// <summary>
/// Plantilla del correo de invitación (asunto, enlace de activación y cuerpo HTML), compartida
/// entre crear invitación (HU #10175, <c>CreateInvitationHandler</c>) y reenviar invitación
/// pendiente (HU #10625, <c>ResendInvitationHandler</c>) — evita duplicar el HTML del correo
/// entre ambos handlers.
/// </summary>
public static class InvitationEmailTemplate
{
    public const string Subject = "Invitación a FLIT — Activa tu cuenta";

    public static string BuildActivateLink(string activateUrlBase, string rawToken)
    {
        var separator = activateUrlBase.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{activateUrlBase}{separator}token={Uri.EscapeDataString(rawToken)}";
    }

    public static string BuildHtmlBody(string fullName, string link, string? assetsBaseUrl = null)
    {
        var name = System.Net.WebUtility.HtmlEncode(fullName);
        var body =
            FlitBrandedEmailLayout.ParagraphHtml($"Hola {name},")
            + FlitBrandedEmailLayout.Paragraph("Has sido invitado a unirte a FLIT.")
            + FlitBrandedEmailLayout.Paragraph(
                "Haz clic en el siguiente enlace para crear tu contraseña y activar tu cuenta:")
            + FlitBrandedEmailLayout.ActionLink(link, "Activar mi cuenta")
            + FlitBrandedEmailLayout.Paragraph(
                "Si no esperabas esta invitación, puedes ignorar este mensaje.");

        return FlitBrandedEmailLayout.Wrap(
            headline: "¡INVITACIÓN A FLIT!",
            bodyInnerHtml: body,
            closingHeadline: "¡Activa tu cuenta y disfruta de todos tus beneficios!",
            assetsBaseUrl: assetsBaseUrl);
    }

    /// <summary>
    /// HU #11351 — composición pura: asunto y cuerpo a partir únicamente de <paramref name="fullName"/>
    /// y <paramref name="link"/> (ya calculado con <see cref="BuildActivateLink"/>). Sin E/S, sin reloj,
    /// sin aleatoriedad, sin estado.
    /// </summary>
    public static ComposedEmail Compose(string fullName, string link, string? assetsBaseUrl = null) =>
        new(Subject, BuildHtmlBody(fullName, link, assetsBaseUrl));
}
