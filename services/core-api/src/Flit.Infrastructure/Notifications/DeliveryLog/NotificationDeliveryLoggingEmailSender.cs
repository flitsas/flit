using System.Diagnostics;
using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Email;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Notifications.DeliveryLog;

/// <summary>
/// Decorador de <see cref="IEmailSender"/> (HU #11363, Feature #11348) — envuelve la implementación
/// real (<see cref="Flit.Infrastructure.Email.SmtpEmailSender"/> o
/// <see cref="Flit.Infrastructure.Email.ConsoleEmailSender"/>) y registra CADA intento en
/// <c>admin.notification_delivery_logs</c>, sin tocar ninguno de los 6 puntos de llamada del puerto
/// (miden duración con <see cref="Stopwatch"/> de forma natural, delegando el envío primero).
/// </summary>
/// <remarks>
/// <para>
/// <b>AC1 — decisión sobre tenant nulo (Opción A, no la B del centinela):</b>
/// <see cref="EmailMessage.TenantId"/> puede ser <c>null</c> (recuperación de contraseña de un
/// usuario sin ninguna asignación de rol activa — ver <c>ForgotPasswordHandler</c>/<c>PasswordRecoveryUser</c>).
/// La columna <c>tenant_id</c> es <c>NOT NULL</c>. Con tenant nulo, ESTA fila <b>no se escribe</b>:
/// queda solo en la traza de aplicación (mismo criterio que ya usa la auditoría de
/// <c>ForgotPasswordHandler</c>, cuya tabla sí admite <c>TenantId: null</c>). Consecuencia
/// explícita: para esos envíos, AC1 ("todo intento deja registro") NO se cumple literalmente —
/// un tenant centinela de plataforma (Opción B) habría preservado la fila, pero cambia la
/// semántica de la columna para una tabla cuyo diseño la declara adrede sin default
/// (ver DDL 64, "un default enmascararía la inserción sin tenant en vez de rechazarla").
/// </para>
/// <para>
/// <b>AC6 — el fallo de bitácora nunca cambia el resultado del envío:</b> el envío YA terminó
/// (<paramref name="inner"/> ya devolvió <see cref="EmailSendResult"/>) antes de intentar escribir
/// la fila. La escritura corre en un <see cref="IServiceScope"/> PROPIO (creado aquí con
/// <see cref="IServiceScopeFactory"/>) — un <c>FlitDbContext</c> nuevo, sin relación con el que
/// pueda estar en uso en el resto de la petición (ver el precedente malo de
/// <c>DbSignatureVaultReader</c>: transacción anidada + <c>try/catch</c> que se tragaba el fallo).
/// El <c>try/catch</c> de aquí SIEMPRE registra el fallo con <see cref="ILogger"/> (nunca lo
/// descarta en silencio) y SIEMPRE devuelve el <see cref="EmailSendResult"/> original, pase lo que
/// pase con la bitácora. Se usa <see cref="CancellationToken.None"/> para la escritura: cancelar la
/// petición HTTP después de que el correo ya salió no debe impedir que quede la evidencia.
/// </para>
/// <para>
/// <b>Canal:</b> el canal escrito en la bitácora es el REALMENTE usado por el envío, no una
/// constante fija. Desde la HU #11362, <see cref="Flit.Infrastructure.Notifications.Routing.TenantChannelEmailRouter"/>
/// se registra como el <paramref name="inner"/> de este decorador (ver
/// <c>InfrastructureExtensions.AddSecurityInfrastructure</c>) y es quien decide, tenant por
/// tenant, por cuál de los dos transportes sale cada envío — este decorador NO vuelve a resolver
/// esa decisión consultando <c>tenant_settings</c> por su cuenta: sería una segunda fuente de
/// verdad que puede divergir del canal que el router realmente usó (p. ej. AC3 del router hace que
/// los correos de cuenta salgan por FlitSmtp aunque el tenant tenga configurado TenantApi). El
/// canal efectivo llega ya resuelto en <see cref="EmailSendResult.Channel"/>; si viene <c>null</c>
/// (un <paramref name="inner"/> que no participa del enrutamiento, p. ej. en un test que envuelve
/// directamente <c>SmtpEmailSender</c>) se asume <see cref="TenantSettingsCodes.ChannelFlitSmtp"/>,
/// el único transporte que existía antes de la HU #11362.
/// </para>
/// </remarks>
internal sealed partial class NotificationDeliveryLoggingEmailSender(
    IEmailSender inner,
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationDeliveryLoggingEmailSender> logger,
    EmailSettings? emailSettings = null) : IEmailSender
{
    // Parámetro opcional (mismo patrón que IEmailThemeResolver? en los handlers de Seguridad, HU
    // #12428): así ningún test existente que construye este decorador con 3 argumentos deja de
    // compilar. DI de producción (InfrastructureExtensions) SIEMPRE pasa la instancia real
    // registrada como singleton.
    private readonly EmailSettings _emailSettings = emailSettings ?? new EmailSettings();

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var stopwatch = Stopwatch.StartNew();
        var result = await inner.SendAsync(message, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        if (message.TenantId is not { } tenantId)
        {
            // Decisión A documentada arriba: sin tenant resoluble, no hay fila que escribir. El
            // aviso SÍ deja el outcome/success del envío (antes se perdía) y NUNCA el destinatario
            // (dato personal, Ley 1581).
            if (result.Success)
                LogSkippedNoTenantSuccess(logger, message.TemplateKey, result.Outcome, result.Success);
            else
                LogSkippedNoTenantFailure(logger, message.TemplateKey, result.Outcome, result.Success);

            return result;
        }

        try
        {
            // Scope propio — ver AC6 en el comentario de la clase.
            using var scope = scopeFactory.CreateScope();
            var writer = scope.ServiceProvider.GetRequiredService<INotificationDeliveryLogWriter>();

            // Canal REALMENTE usado (ver comentario de clase) — nunca una constante fija.
            var effectiveChannel = result.Channel ?? TenantSettingsCodes.ChannelFlitSmtp;
            // HU #12430 AC2/AC5 — el remitente solo se traza para el canal FlitSmtp: es el único
            // donde este proceso conoce con certeza la dirección aplicada
            // (EmailSettings.DefaultSenderEmail, fija por ambiente). El canal Renting (TenantApi)
            // aplica su PROPIO remitente configurado (RentingChannelOptions, ver
            // TenantChannelEmailRouter) — este decorador no lo conoce sin acoplarse a ese canal, así
            // que registra NULL en vez de arriesgar un valor incorrecto.
            var isFlitSmtp = effectiveChannel == TenantSettingsCodes.ChannelFlitSmtp;
            var appliedSenderName = isFlitSmtp
                ? SenderDisplayNameSanitizer.Sanitize(message.SenderDisplayName) ?? _emailSettings.DefaultSenderName
                : null;
            var appliedSenderEmail = isFlitSmtp ? _emailSettings.DefaultSenderEmail : null;

            await writer.WriteAsync(
                new NotificationDeliveryLogEntry(
                    tenantId,
                    message.TemplateKey,
                    effectiveChannel,
                    message.ToEmail,
                    result.Success,
                    result.Success ? null : result.Message,
                    (int)Math.Clamp(stopwatch.ElapsedMilliseconds, 0, int.MaxValue),
                    // HU #11364 AC2 — el destinatario ORIGINAL ya es message.ToEmail (arriba): esta
                    // marca es lo único que faltaba para que la fila no afirme, falsamente, que el
                    // correo llegó a ese destinatario.
                    result.RecipientDiverted)
                {
                    // HU #12428 AC5 / #12430 AC5 — el tema y el remitente YA se resolvieron al
                    // componer message.HtmlBody; este decorador solo los traslada a la bitácora.
                    ThemeKind = message.ThemeKind,
                    ThemeVersion = message.ThemeVersion,
                    SenderName = appliedSenderName,
                    SenderEmail = appliedSenderEmail,
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // AC6 — el fallo se registra de verdad en la traza de aplicación; el resultado del
            // envío (ya calculado arriba) se devuelve intacto, sin importar qué pasó aquí.
            LogWriteFailed(logger, ex, message.TemplateKey, result.Channel ?? TenantSettingsCodes.ChannelFlitSmtp);
        }

        return result;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "No se registró el intento de envío en la bitácora de notificaciones: sin tenant "
            + "resoluble (plantilla={TemplateKey}, resultado={Outcome}, éxito={Success}). AC1 no "
            + "aplica a este envío.")]
    private static partial void LogSkippedNoTenantSuccess(
        ILogger logger, string templateKey, EmailSendOutcome outcome, bool success);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "No se registró el intento de envío en la bitácora de notificaciones: sin tenant "
            + "resoluble (plantilla={TemplateKey}, resultado={Outcome}, éxito={Success}). AC1 no "
            + "aplica a este envío.")]
    private static partial void LogSkippedNoTenantFailure(
        ILogger logger, string templateKey, EmailSendOutcome outcome, bool success);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "No fue posible registrar el intento de envío en la bitácora de notificaciones "
            + "(plantilla={TemplateKey}, canal={Channel}). El envío en sí NO se ve afectado.")]
    private static partial void LogWriteFailed(ILogger logger, Exception exception, string templateKey, string channel);
}
