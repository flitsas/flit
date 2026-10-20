namespace Flit.Modules.Security.Application.Auth;

/// <summary>
/// Plantilla del correo de invitación (asunto, enlace de activación y cuerpo HTML), compartida
/// entre crear invitación (HU #10175, <c>CreateInvitationHandler</c>) y reenviar invitación
/// pendiente (HU #10625, <c>ResendInvitationHandler</c>) — evita duplicar el HTML del correo
/// entre ambos handlers.
/// </summary>
public static class InvitationEmailTemplate
{
    /// <summary>Asunto con el tema FLIT. Con tema de marca ver <see cref="BuildSubject"/>.</summary>
    public const string Subject = "Invitación a FLIT — Activa tu cuenta";

    private const string FlitName = "FLIT";

    public static string BuildActivateLink(string activateUrlBase, string rawToken)
    {
        var separator = activateUrlBase.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{activateUrlBase}{separator}token={Uri.EscapeDataString(rawToken)}";
    }

    /// <summary>
    /// Asunto: con tema de marca invita a la compañía Marca Blanca, no a FLIT; con tema FLIT (o
    /// sin tema) es exactamente <see cref="Subject"/>.
    /// </summary>
    public static string BuildSubject(Flit.Modules.Security.Domain.Auth.EmailTheme? theme = null) =>
        $"Invitación a {InviterName(theme)} — Activa tu cuenta";

    public static string BuildHtmlBody(
        string fullName,
        string link,
        string? assetsBaseUrl = null,
        Flit.Modules.Security.Domain.Auth.EmailTheme? theme = null)
    {
        var name = System.Net.WebUtility.HtmlEncode(fullName);
        var inviter = InviterName(theme);
        var linkColor = theme is { IsBrand: true }
            ? Flit.Modules.Security.Domain.Auth.BrandedEmailChrome.LinkColor(theme)
            : null;
        var body =
            FlitBrandedEmailLayout.ParagraphHtml($"Hola {name},")
            + FlitBrandedEmailLayout.Paragraph($"Has sido invitado a unirte a {inviter}.")
            + FlitBrandedEmailLayout.Paragraph(
                "Haz clic en el siguiente enlace para crear tu contraseña y activar tu cuenta:")
            + FlitBrandedEmailLayout.ActionLink(link, "Activar mi cuenta", linkColor)
            + FlitBrandedEmailLayout.Paragraph(
                "Si no esperabas esta invitación, puedes ignorar este mensaje.");

        return FlitBrandedEmailLayout.Wrap(
            headline: $"¡INVITACIÓN A {inviter.ToUpperInvariant()}!",
            bodyInnerHtml: body,
            closingHeadline: "¡Activa tu cuenta y disfruta de todos tus beneficios!",
            assetsBaseUrl: assetsBaseUrl,
            theme: theme);
    }

    /// <summary>
    /// HU #11351 — composición pura: asunto y cuerpo a partir únicamente de <paramref name="fullName"/>
    /// y <paramref name="link"/> (ya calculado con <see cref="BuildActivateLink"/>). Sin E/S, sin reloj,
    /// sin aleatoriedad, sin estado. <paramref name="theme"/> (HU #12428) es opcional y aditivo: sin
    /// él, o con el tema FLIT, la salida es idéntica a antes de esta historia.
    /// </summary>
    public static ComposedEmail Compose(
        string fullName,
        string link,
        string? assetsBaseUrl = null,
        Flit.Modules.Security.Domain.Auth.EmailTheme? theme = null) =>
        new(BuildSubject(theme), BuildHtmlBody(fullName, link, assetsBaseUrl, theme));

    /// <summary>Nombre de la marca que invita; FLIT si el tema no es de marca o viene sin nombre.</summary>
    private static string InviterName(Flit.Modules.Security.Domain.Auth.EmailTheme? theme) =>
        theme is { IsBrand: true } && !string.IsNullOrWhiteSpace(theme.PlatformName)
            ? theme.PlatformName.Trim()
            : FlitName;
}
