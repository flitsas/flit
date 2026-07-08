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

    public static string BuildHtmlBody(string fullName, string link) => $"""
        <p>Hola {System.Net.WebUtility.HtmlEncode(fullName)},</p>
        <p>Has sido invitado a unirte a FLIT.</p>
        <p>Haz clic en el siguiente enlace para crear tu contraseña y activar tu cuenta:</p>
        <p><a href="{link}">Activar mi cuenta</a></p>
        <p>Si no esperabas esta invitación, puedes ignorar este mensaje.</p>
        <p>— Equipo FLIT</p>
        """;
}
