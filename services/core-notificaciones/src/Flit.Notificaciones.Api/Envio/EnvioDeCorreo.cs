using System.Diagnostics;
using Flit.Infrastructure.Notifications.Renting;
using Flit.Modules.Notificaciones;
using Flit.Modules.Security.Domain.Auth;
using Flit.Notificaciones.Api.Persistence;

namespace Flit.Notificaciones.Api.Envio;

/// <summary>Un correo ya armado por quien lo pide, con el canal que ese servicio resolvió (decisión del Feature #13324).</summary>
/// <param name="Origen">Servicio que lo pide (producto del token o productor del trabajo).</param>
/// <param name="TrabajoId">Trabajo del bus que lo originó; null si llegó por gRPC.</param>
/// <param name="BuzonControlado">Solo el buzón de pruebas: su destinatario no se desvía (HU #11372).</param>
internal sealed record PedidoDeEnvio(EmailMessage Mensaje, CanalCorreo Canal, string Origen, Guid? TrabajoId = null, bool BuzonControlado = false);

/// <summary>
/// HU #13353 — envía por el canal pedido (<see cref="CorreoPorCanal"/>, la misma regla que core-api) y deja el intento en
/// <c>notificaciones.entregas</c>, salga bien o mal (AC2: un canal sin credenciales queda registrado como fallido y el
/// pedido no se pierde: el resultado vuelve a quien lo pidió y el trabajo del bus se reintenta). Nunca lanza por el
/// transporte.
/// </summary>
internal sealed partial class EnvioDeCorreo(CorreoPorCanal porCanal, NotificacionesDb db, TimeProvider time, ILogger<EnvioDeCorreo> logger)
{
    public async Task<(EmailSendResult Resultado, Guid EntregaId)> EnviarAsync(PedidoDeEnvio pedido, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        var reloj = Stopwatch.StartNew();
        EmailSendResult resultado;
        try
        {
            resultado = await porCanal.SendAsync(
                pedido.Canal, pedido.Mensaje, pedido.BuzonControlado ? ControlledMailboxRecipient.Instance : null, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Los transportes no lanzan por contrato; si uno lo hace, cuenta como proveedor caído y se registra.
            LogTransporteLanzo(logger, pedido.Mensaje.TemplateKey, ex);
            resultado = EmailSendResult.Failed(EmailSendOutcome.ProviderUnavailable) with
            {
                Channel = CanalCorreoCodigos.De(pedido.Canal),
            };
        }

        var entrega = new Entrega
        {
            Id = Guid.CreateVersion7(),
            TenantId = pedido.Mensaje.TenantId,
            Plantilla = Recortar(pedido.Mensaje.TemplateKey, 100),
            Canal = resultado.Channel ?? CanalCorreoCodigos.De(pedido.Canal),
            Destinatario = Recortar(pedido.Mensaje.ToEmail, 320),
            Resultado = resultado.Success ? "enviado" : "fallido",
            Desenlace = resultado.Outcome.ToString(),
            MotivoFallo = resultado.Success ? null : Recortar(resultado.Message + CausaDeCorreo.ConDetalle(resultado.Detalle), 1000),
            DuracionMs = (int)Math.Min(int.MaxValue, reloj.ElapsedMilliseconds),
            Desviado = resultado.RecipientDiverted,
            Tema = pedido.Mensaje.ThemeKind,
            TemaVersion = pedido.Mensaje.ThemeVersion,
            RemitenteNombre = pedido.Mensaje.SenderDisplayName is { } r ? Recortar(r, 80) : null,
            Origen = Recortar(pedido.Origen, 40),
            TrabajoId = pedido.TrabajoId,
            OcurridoEn = time.GetUtcNow(),
        };
        db.Entregas.Add(entrega);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (resultado, entrega.Id);
    }

    private static string Recortar(string valor, int max) => valor.Length <= max ? valor : valor[..max];

    [LoggerMessage(EventId = 7501, Level = LogLevel.Error, Message = "El transporte lanzó al enviar {Plantilla}; se registra como proveedor no disponible")]
    private static partial void LogTransporteLanzo(ILogger logger, string plantilla, Exception ex);
}
