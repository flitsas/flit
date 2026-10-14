using Flit.Modules.Notificaciones;
using Flit.Notificaciones.Api.Webhooks;
using Flit.Notificaciones.Grpc.V1;
using Flit.Platform.Sdk.Messaging;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Flit.Notificaciones.Api.Grpc;

/// <summary>
/// <c>flit.notificaciones.v1.MensajesMuertosService</c> (HU #13357): los correos y webhooks que agotaron sus reintentos,
/// para que el SuperAdmin los reintente o descarte desde core-api (<see cref="PlatformDeadLetters"/>).
/// </summary>
internal sealed class MensajesMuertosGrpcService(PlatformDeadLetters muertos) : MensajesMuertosService.MensajesMuertosServiceBase
{
    public const string Scope = "platform.notificaciones.admin";

    public override async Task<ListarMensajesMuertosResponse> ListarMensajesMuertos(ListarMensajesMuertosRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var limite = request.Limite is > 0 and <= 200 ? request.Limite : 100;
        var lista = await muertos.ListAsync(Cola(request.Cola), limite, context.CancellationToken).ConfigureAwait(false);
        var respuesta = new ListarMensajesMuertosResponse();
        foreach (var m in lista)
        {
            var mensaje = new MensajeMuerto { Id = m.MessageId, Tipo = m.Type, Intentos = m.Attempts };
            if (m.TenantId is { } tenant)
                mensaje.TenantId = tenant.ToString();
            if (m.Producer is not null)
                mensaje.Productor = m.Producer;
            if (m.OccurredAt is { } ocurrido)
                mensaje.OcurridoEn = Timestamp.FromDateTimeOffset(ocurrido);
            if (m.DeadLetteredAt is { } muerto)
                mensaje.MuertoEn = Timestamp.FromDateTimeOffset(muerto);
            if (m.Reason is not null)
                mensaje.Motivo = m.Reason;
            if (m.Error is not null)
                mensaje.UltimoError = m.Error;
            respuesta.Mensajes.Add(mensaje);
        }

        return respuesta;
    }

    public override async Task<ReintentarMensajeMuertoResponse> ReintentarMensajeMuerto(ReintentarMensajeMuertoRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        if (!await muertos.RetryAsync(Cola(request.Cola), request.Id, context.CancellationToken).ConfigureAwait(false))
            throw NoEsta(request.Id);
        return new ReintentarMensajeMuertoResponse();
    }

    public override async Task<DescartarMensajeMuertoResponse> DescartarMensajeMuerto(DescartarMensajeMuertoRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        if (!await muertos.DiscardAsync(Cola(request.Cola), request.Id, context.CancellationToken).ConfigureAwait(false))
            throw NoEsta(request.Id);
        return new DescartarMensajeMuertoResponse();
    }

    /// <summary>Solo las colas de este servicio (la .dlq es de cada consumidor).</summary>
    private static string Cola(ColaMuertos cola) => cola switch
    {
        ColaMuertos.Correos => TrabajoCorreo.Tipo,
        ColaMuertos.Webhooks => TrabajoWebhookConsumer.Cola,
        _ => throw new RpcException(new Status(StatusCode.InvalidArgument, "Cola desconocida: correos o webhooks.")),
    };

    private static RpcException NoEsta(string id) =>
        new(new Status(StatusCode.NotFound, $"El mensaje {id} ya no está en la cola de mensajes muertos."));
}
