using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Notifications.Renting;

/// <summary>
/// Punto de encaje para la HU #11364 (desvío de destinatario fuera de producción — FUERA DE
/// ALCANCE de la HU #11361, que solo modela el hueco donde esa HU se conecta). Se invoca DESPUÉS
/// de construir el <see cref="RentingSendEmailRequest"/> definitivo y ANTES de construir el
/// multipart (ver <see cref="RentingEmailApiSender.SendAsync"/>), para que la sustitución de
/// destinatarios (reales → <c>RENTING_API_SEND_EMAIL_DEVELOPMENT_RECIPIENT_*</c>, ver
/// <see cref="RentingChannelOptions"/>) no tenga que reimplementar nada del armado del cuerpo ni
/// del envío/autenticación.
/// </summary>
public interface IRentingRecipientOverride
{
    /// <summary>
    /// Devuelve la petición a enviar, junto con si se desvió (HU #11364 AC2): el llamador
    /// (<see cref="RentingEmailApiSender.SendAsync"/>) necesita ese booleano para marcar el
    /// <see cref="Flit.Modules.Security.Domain.Auth.EmailSendResult.RecipientDiverted"/> que sube
    /// hasta la bitácora — el resultado del envío es el único vehículo que atraviesa el decorador de
    /// arriba (HU #11363), así que el desvío tiene que viajar en él. La implementación por defecto
    /// (<see cref="PassthroughRentingRecipientOverride"/>) no modifica nada y siempre reporta
    /// <c>Diverted = false</c> — la HU #11364 registra la implementación real (gate por ambiente) sin
    /// que esta HU tenga que anticiparla.
    /// </summary>
    RentingRecipientOverrideResult Apply(RentingSendEmailRequest request);
}

/// <summary>
/// HU #11364 AC2 — resultado de <see cref="IRentingRecipientOverride.Apply"/>: la petición a enviar
/// (ya con el destinatario sustituido si <see cref="Diverted"/> es <c>true</c>) y si hubo desvío.
/// </summary>
public readonly record struct RentingRecipientOverrideResult(RentingSendEmailRequest Request, bool Diverted);

/// <summary>
/// Implementación por defecto de <see cref="IRentingRecipientOverride"/>: no desvía nada. Es la
/// registrada por la HU #11361 cuando el canal está deshabilitado (rama temprana de
/// <c>InfrastructureExtensions.AddRentingChannel</c>, que no llega a registrar
/// <see cref="IRentingRecipientOverride"/> en absoluto); con el canal habilitado, la HU #11364
/// registra siempre <see cref="RentingRecipientOverride"/> en su lugar.
/// </summary>
internal sealed class PassthroughRentingRecipientOverride : IRentingRecipientOverride
{
    public RentingRecipientOverrideResult Apply(RentingSendEmailRequest request) => new(request, Diverted: false);
}

/// <summary>
/// HU #11364 — desvío OBLIGATORIO de destinatario fuera de producción. Único mecanismo que impide
/// que un envío de desarrollo/QA por el canal Renting llegue a un cliente final real: los tres
/// <c>.pfx</c> "de pruebas" del repositorio del cliente son el MISMO archivo byte a byte — no hay
/// ambiente de pruebas de Renting, solo producción.
///
/// <para>
/// AC5 — la decisión de desviar se toma ÚNICAMENTE por
/// <see cref="RentingChannelOptions.SendEmailDevelopmentRecipientOverrideEnabled"/> (el interruptor
/// afirmativo y propio de esta HU). Esta clase NUNCA consulta <c>IHostEnvironment</c> ni el nombre
/// del ambiente: esa validación (AC3/AC4) vive exclusivamente en el arranque
/// (<c>InfrastructureExtensions.AddRentingChannel</c>), no aquí.
/// </para>
///
/// <para>
/// AC6 — al vivir DENTRO del adaptador del canal Renting (y ser consumida únicamente por
/// <see cref="RentingEmailApiSender.SendAsync"/>), el SMTP de FLIT (<c>SmtpEmailSender</c>) queda
/// ESTRUCTURALMENTE fuera de su alcance: no hay una condición que alguien pueda desactivar por
/// error, sino que el flujo SMTP nunca invoca <see cref="IRentingRecipientOverride"/>.
/// </para>
/// </summary>
internal sealed class RentingRecipientOverride(
    IOptions<RentingChannelOptions> options,
    ILogger<RentingRecipientOverride> logger) : IRentingRecipientOverride
{
    public RentingRecipientOverrideResult Apply(RentingSendEmailRequest request)
    {
        var o = options.Value;
        if (!o.SendEmailDevelopmentRecipientOverrideEnabled)
            return new RentingRecipientOverrideResult(request, Diverted: false);

        // Log de aplicación (aviso operativo, se rota y no es evidencia auditable). AC2 lo cumple la
        // BITÁCORA (admin.notification_delivery_logs.recipient_diverted, HU #11363): el destinatario
        // original YA queda en esa fila (el decorador lee EmailMessage.ToEmail ANTES de que este
        // desvío actúe); lo que falta, y que este log NO provee, es la marca — la aporta el booleano
        // Diverted de este resultado, que RentingEmailApiSender sube hasta EmailSendResult.
        var originalRecipients = string.Join(", ", request.Recipients.Select(r => r.Email));
        var originalBcc = string.Join(", ", request.BccRecipients);
        RentingRecipientOverrideLog.RecipientDiverted(logger, originalRecipients, originalBcc);

        var overrideRecipient = new RentingEmailAddress(
            o.SendEmailDevelopmentRecipientEmail, o.SendEmailDevelopmentRecipientUsername);

        // AC1 — el destinatario de desvío reemplaza TODOS los destinatarios de la petición
        // (principales y copia oculta): el original no puede aparecer en NINGÚN campo de
        // destinatario de la petición al proveedor.
        var diverted = request with
        {
            Recipients = [overrideRecipient],
            BccRecipients = [],
        };

        return new RentingRecipientOverrideResult(diverted, Diverted: true);
    }
}

/// <summary>Logging source-generated (CA1848) del desvío de destinatario (HU #11364, AC2).</summary>
internal static partial class RentingRecipientOverrideLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Canal Renting: ENVÍO DESVIADO (desarrollo/QA). Destinatario(s) original(es): {OriginalRecipients}. "
            + "Copia oculta original: {OriginalBcc}.")]
    public static partial void RecipientDiverted(ILogger logger, string originalRecipients, string originalBcc);
}
