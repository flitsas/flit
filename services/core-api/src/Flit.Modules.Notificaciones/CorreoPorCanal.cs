using Flit.Infrastructure.Notifications.Renting;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.Extensions.Options;

namespace Flit.Modules.Notificaciones;

/// <summary>Por dónde sale un correo: el SMTP (o la consola) de FLIT, o la API de correo de la empresa (Renting).</summary>
public enum CanalCorreo
{
    FlitSmtp,
    EmpresaApi,
}

/// <summary>Códigos de canal en el registro de entregas (los mismos de <c>admin.tenant_operational_policies</c>).</summary>
public static class CanalCorreoCodigos
{
    public const string FlitSmtp = "flit_smtp";
    public const string EmpresaApi = "tenant_api";

    public static string De(CanalCorreo canal) => canal == CanalCorreo.EmpresaApi ? EmpresaApi : FlitSmtp;
}

/// <summary>
/// HU #13353 — envía un correo ya armado por un canal ya decidido. Es la regla del enrutador de core-api
/// (<c>TenantChannelEmailRouter</c>) sin la resolución del canal, para que core-api y core-notificaciones envíen igual:
/// <list type="bullet">
/// <item>FLIT: el transporte de FLIT (SMTP o consola), con su remitente.</item>
/// <item>Empresa: la API de Renting, con el remitente configurado para ese canal (nunca el de la marca). Sin el canal
/// habilitado en el ambiente responde <see cref="EmailSendOutcome.ConfigurationIncomplete"/>: nunca cae a SMTP en
/// silencio (HU #11359 AC6).</item>
/// </list>
/// Nunca lanza por el transporte: el resultado lo dice.
/// </summary>
public sealed class CorreoPorCanal(IEmailSender flitTransport, IRentingEmailApiSender? renting, IOptions<RentingChannelOptions> rentingOptions)
{
    /// <summary>FLIT siempre está; la API de la empresa solo si el canal Renting está habilitado en el ambiente.</summary>
    public bool Disponible(CanalCorreo canal) => canal != CanalCorreo.EmpresaApi || renting is not null;

    /// <param name="exencion">
    /// Solo el buzón de pruebas: el destinatario es un buzón controlado y no se desvía (HU #11372). Los envíos reales
    /// pasan null y el desvío de destinatarios fuera de PDN (HU #11364) aplica siempre.
    /// </param>
    public async Task<EmailSendResult> SendAsync(CanalCorreo canal, EmailMessage message, ControlledMailboxRecipient? exencion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (canal != CanalCorreo.EmpresaApi)
        {
            var flit = await flitTransport.SendAsync(message, cancellationToken).ConfigureAwait(false);
            return flit with { Channel = CanalCorreoCodigos.FlitSmtp };
        }

        if (renting is null)
            return EmailSendResult.Failed(EmailSendOutcome.ConfigurationIncomplete) with { Channel = CanalCorreoCodigos.EmpresaApi };

        var options = rentingOptions.Value;
        // HU #12430 AC2 — el canal Renting ignora message.SenderDisplayName: su remitente es siempre el configurado.
        var request = new RentingSendEmailRequest(
            message.Subject,
            message.HtmlBody,
            new RentingEmailAddress(options.SendEmailSenderEmail, options.SendEmailSenderUsername),
            [new RentingEmailAddress(message.ToEmail, message.ToName)],
            message.BccEmails.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()).ToList(),
            message.Attachments.Select(a => new RentingEmailAttachment(a.FileName, a.ContentType, a.Content)).ToList());

        var resultado = exencion is not null
            ? await renting.SendAsync(request, exencion, cancellationToken).ConfigureAwait(false)
            : await renting.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return resultado with { Channel = CanalCorreoCodigos.EmpresaApi };
    }
}
