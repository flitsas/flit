using Flit.Modules.Security.Domain.Auth;

namespace Flit.Modules.Notificaciones;

/// <summary>
/// HU #13354 — datos del trabajo <see cref="Tipo"/> (contracts/asyncapi/notificaciones-events.v1.yaml): un correo ya
/// armado por quien lo pide, con el canal que ese servicio resolvió (decisión del Feature #13324). Lo arma core-api o
/// core-identity con <see cref="De"/> y lo envía core-notificaciones con <see cref="AMensaje"/>.
/// </summary>
public sealed record TrabajoCorreo(
    string Plantilla,
    string Canal,
    TrabajoCorreo.Persona Destinatario,
    string Asunto,
    string Html,
    IReadOnlyList<TrabajoCorreo.Adjunto>? Adjuntos = null,
    IReadOnlyList<string>? CopiaOculta = null,
    string? RemitenteNombre = null,
    string? Tema = null,
    int? TemaVersion = null)
{
    public const string Tipo = "notificaciones.email.send";

    public sealed record Persona(string Email, string Nombre);

    /// <summary><c>ContenidoBase64</c>: el arreglo de bytes viaja en base64 en el JSON.</summary>
    public sealed record Adjunto(string Nombre, string ContentType, byte[] ContenidoBase64);

    public static TrabajoCorreo De(EmailMessage mensaje, CanalCorreo canal)
    {
        ArgumentNullException.ThrowIfNull(mensaje);
        return new TrabajoCorreo(
            mensaje.TemplateKey,
            CanalCorreoCodigos.De(canal),
            new Persona(mensaje.ToEmail, mensaje.ToName),
            mensaje.Subject,
            mensaje.HtmlBody,
            mensaje.Attachments.Count == 0 ? null : [.. mensaje.Attachments.Select(a => new Adjunto(a.FileName, a.ContentType, a.Content))],
            mensaje.BccEmails.Count == 0 ? null : mensaje.BccEmails,
            mensaje.SenderDisplayName,
            mensaje.ThemeKind,
            mensaje.ThemeVersion);
    }

    public CanalCorreo CanalCorreo => string.Equals(Canal, CanalCorreoCodigos.EmpresaApi, StringComparison.Ordinal)
        ? Notificaciones.CanalCorreo.EmpresaApi
        : Notificaciones.CanalCorreo.FlitSmtp;

    public EmailMessage AMensaje(Guid? tenantId) =>
        new(tenantId, Plantilla, Destinatario.Email, Destinatario.Nombre, Asunto, Html)
        {
            Attachments = Adjuntos is null ? [] : [.. Adjuntos.Select(a => new EmailAttachment(a.Nombre, a.ContentType, a.ContenidoBase64))],
            BccEmails = CopiaOculta ?? [],
            SenderDisplayName = RemitenteNombre,
            ThemeKind = Tema,
            ThemeVersion = TemaVersion,
        };
}
