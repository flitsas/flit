namespace Flit.Infrastructure.Notifications.Renting;

/// <summary>
/// HU #11361 — remitente o destinatario del contrato de envío del proveedor Renting: siempre
/// viaja como el par <c>Email</c>/<c>UserName</c> (ver <see cref="RentingSendEmailRequest"/>).
/// </summary>
public sealed record RentingEmailAddress(string Email, string UserName);

/// <summary>
/// HU #11361 — archivo adjunto del envío. <see cref="ContentType"/> vacío se resuelve como
/// <c>application/octet-stream</c> al construir el multipart (ver
/// <see cref="RentingEmailApiSender.BuildMultipartContent"/>).
/// </summary>
public sealed record RentingEmailAttachment(string FileName, string ContentType, byte[] Content);

/// <summary>
/// HU #11361 — petición de envío del canal Renting, ya independiente del contrato genérico
/// <see cref="Flit.Modules.Security.Domain.Auth.EmailMessage"/> (que solo modela un destinatario y
/// no modela copia oculta ni adjuntos): quien enruta hacia este canal (HU #11362) es responsable
/// de construir este tipo a partir de lo que tenga disponible.
/// </summary>
/// <param name="Subject">Asunto del correo.</param>
/// <param name="Body">Cuerpo del correo (HTML o texto, según lo que produzca el llamador).</param>
/// <param name="Sender">Remitente. Normalmente <c>RENTING_API_SEND_EMAIL_SENDER_*</c>.</param>
/// <param name="Recipients">
/// Destinatarios principales. Cada uno viaja indexado como <c>Recipients[i].Email</c> /
/// <c>Recipients[i].UserName</c> (AC1).
/// </param>
/// <param name="BccRecipients">
/// Correos en copia oculta. Cada uno viaja indexado como <c>bccRecipients[i].Email</c> — el
/// contrato del proveedor NO define un campo de nombre para copia oculta (AC1/AC2). El índice es
/// SIEMPRE incremental por posición en la lista: réplica del defecto de la implementación de
/// referencia (que escribe siempre en el índice 0) está explícitamente prohibida.
/// </param>
/// <param name="Attachments">Archivos adjuntos, cada uno bajo el campo <c>AttachFiles</c>.</param>
public sealed record RentingSendEmailRequest(
    string Subject,
    string Body,
    RentingEmailAddress Sender,
    IReadOnlyList<RentingEmailAddress> Recipients,
    IReadOnlyList<string> BccRecipients,
    IReadOnlyList<RentingEmailAttachment> Attachments)
{
    /// <summary>Atajo para el caso más común: un solo destinatario, sin copia oculta ni adjuntos.</summary>
    public static RentingSendEmailRequest ToSingleRecipient(
        string subject, string body, RentingEmailAddress sender, RentingEmailAddress recipient) =>
        new(subject, body, sender, [recipient], [], []);
}
