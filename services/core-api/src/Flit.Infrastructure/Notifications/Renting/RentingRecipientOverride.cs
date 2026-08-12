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
/// HU #11364 / ADR-0044 — desvío al buzón de control por defecto en todo despliegue que no declare
/// envío real. Único mecanismo que impide que un envío por el canal Renting llegue a un cliente
/// final real cuando el despliegue no lo pidió explícitamente: los tres <c>.pfx</c> "de pruebas"
/// del repositorio del cliente son el MISMO archivo byte a byte — no hay ambiente de pruebas de
/// Renting, solo producción.
///
/// <para>
/// ADR-0044 — la decisión de desviar se toma ÚNICAMENTE por
/// <see cref="RentingChannelOptions.DivertRecipientsEnabled"/> (calculado en el arranque a partir
/// de la variable afirmativa y propia del despliegue,
/// <c>RENTING_API_SEND_EMAIL_REAL_RECIPIENTS_ENABLED</c>). Esta clase NUNCA consulta
/// <c>IHostEnvironment</c> ni el nombre del ambiente: esa consulta desapareció del canal por
/// completo (ADR-0044 deroga los AC3/AC4 de la HU #11364, que sí lo hacían).
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
        if (!o.DivertRecipientsEnabled)
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

/// <summary>
/// HU #11372 (decisión del PO 2026-08-11) — marca que el destinatario de la petición YA es un buzón
/// CONTROLADO: no dato de un cliente final, sino el correo único que un SuperAdmin configuró
/// explícitamente en la UI del banco de pruebas de notificaciones
/// (<c>admin.notification_test_settings.test_recipient_email</c>). El desvío OBLIGATORIO de
/// <see cref="RentingRecipientOverride"/> (HU #11364) existe para impedir que un envío de
/// desarrollo/QA alcance a un CLIENTE FINAL real, porque el endpoint de Renting es el de
/// PRODUCCIÓN (no hay ambiente de pruebas de Renting). Ese riesgo no existe para el banco de
/// pruebas: desviar su buzón no protege a nadie y rompe el propósito del módulo (probar de verdad
/// el canal TENANT_API).
///
/// <para>
/// <b>Por qué esta exención NO debilita la barrera de la HU #11364.</b> Es alcanzable ÚNICAMENTE
/// desde <see cref="Routing.IExplicitChannelEmailSender.SendAsync(Routing.NotificationChannel, Flit.Modules.Security.Domain.Auth.EmailMessage, CancellationToken)"/>
/// — el único camino que construye esta instancia y la propaga hasta
/// <see cref="IRentingEmailApiSender.SendAsync(RentingSendEmailRequest, ControlledMailboxRecipient, CancellationToken)"/>.
/// El camino de producción (<see cref="Flit.Modules.Security.Domain.Auth.IEmailSender.SendAsync"/>,
/// los 6 puntos de envío reales, resuelto internamente por
/// <c>Routing.TenantChannelEmailRouter.SendAsync(Flit.Modules.Security.Domain.Auth.EmailMessage, CancellationToken)</c>)
/// NUNCA construye ni recibe esta marca: no existe un parámetro, un flag ni una rama por la que
/// pudiera colarse. No es una convención que alguien pueda invertir por error — es que el método de
/// una vía del router sencillamente no la propaga, así que el envío sigue pasando SIEMPRE por
/// <see cref="RentingRecipientOverride"/> con la misma regla de la HU #11364 (AC3/AC4/AC5).
/// </para>
/// </summary>
public sealed class ControlledMailboxRecipient
{
    /// <summary>Única instancia: no hay estado que transportar, solo la marca de tipo.</summary>
    public static readonly ControlledMailboxRecipient Instance = new();

    private ControlledMailboxRecipient()
    {
    }
}
