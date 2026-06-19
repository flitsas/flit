namespace Flit.Modules.Security.Domain.Auth;

/// <summary>Mensaje de correo listo para enviar (cuerpo HTML ya compuesto).</summary>
public sealed record EmailMessage(string ToEmail, string ToName, string Subject, string HtmlBody);

/// <summary>
/// Puerto de envío de correo. La implementación (SMTP/MailKit o consola en dev) vive
/// en la capa de infraestructura.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
