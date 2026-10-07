using Flit.Modules.Notificaciones;
using Flit.Modules.Security.Domain.Auth;
using Flit.Platform.Sdk.Messaging;

namespace Flit.Notificaciones.Api.Envio;

/// <summary>
/// HU #13354 — atiende <see cref="TrabajoCorreo.Tipo"/> desde la cola <see cref="Cola"/>. Cada intento queda en el
/// registro de entregas en su propia transacción (un reintento no borra el intento fallido). Lo que puede resolverse solo
/// (proveedor caído, tiempo agotado, límite del proveedor, credenciales o canal sin configurar) se reintenta a 10 s,
/// 1 min y 10 min y luego queda en <c>notificaciones.email.send.dlq</c>, de donde el SuperAdmin lo reintenta (AC2). Un
/// rechazo del destinatario o del contenido no mejora reintentando solo, pero puede venir de una causa nuestra que se
/// corrige después (HU #13359, visto en la prueba en vivo: el buzón remitente lleno da «5.2.2 Mailbox full» y llega como
/// rechazo del contenido): va directo a la <c>.dlq</c>, sin reintentos automáticos, para reintentarlo a mano una vez
/// corregida la causa.
/// </summary>
internal sealed partial class TrabajoCorreoConsumer(IServiceScopeFactory scopes, ILogger<TrabajoCorreoConsumer> logger) : IEventConsumer<TrabajoCorreo>
{
    public const string Cola = TrabajoCorreo.Tipo;

    public async Task HandleAsync(EventEnvelope envelope, TrabajoCorreo data, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(data);

        // Scope propio: la entrega se confirma aunque este intento termine en reintento (la bandeja hace rollback).
        await using var scope = scopes.CreateAsyncScope();
        var envio = scope.ServiceProvider.GetRequiredService<EnvioDeCorreo>();
        var (resultado, entregaId) = await envio.EnviarAsync(
            new PedidoDeEnvio(data.AMensaje(envelope.TenantId), data.CanalCorreo, envelope.Producer, envelope.EventId), ct).ConfigureAwait(false);

        if (resultado.Success)
            return;
        if (SeReintenta(resultado.Outcome))
            throw new CorreoNoEnviadoException(resultado.Outcome, entregaId);

        LogRechazado(logger, envelope.EventId, data.Plantilla, resultado.Outcome, entregaId);
        throw new CorreoRechazadoException(resultado.Outcome, entregaId, resultado.Message);
    }

    internal static bool SeReintenta(EmailSendOutcome outcome) => outcome is not (EmailSendOutcome.RecipientRejected or EmailSendOutcome.ContentRejected);

    [LoggerMessage(EventId = 7511, Level = LogLevel.Warning,
        Message = "Trabajo de correo {EventId} ({Plantilla}) rechazado por el proveedor ({Desenlace}); va a mensajes muertos sin reintento automático. Entrega {EntregaId}")]
    private static partial void LogRechazado(ILogger logger, Guid eventId, string plantilla, EmailSendOutcome desenlace, Guid entregaId);
}

/// <summary>El transporte no pudo enviar por una causa que puede pasar: el consumidor del SDK lo reintenta.</summary>
internal sealed class CorreoNoEnviadoException(EmailSendOutcome desenlace, Guid entregaId)
    : Exception($"El correo no salió ({desenlace}); entrega {entregaId}. Se reintenta.")
{
    public EmailSendOutcome Desenlace { get; } = desenlace;
}

/// <summary>El proveedor rechazó el correo: no se reintenta solo, queda en mensajes muertos para reintentarlo a mano.</summary>
internal sealed class CorreoRechazadoException(EmailSendOutcome desenlace, Guid entregaId, string? motivo)
    : SinReintentoAutomaticoException($"El proveedor rechazó el correo ({desenlace}): {motivo}; entrega {entregaId}.")
{
    public EmailSendOutcome Desenlace { get; } = desenlace;
}
