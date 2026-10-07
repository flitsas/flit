using Flit.Modules.Notificaciones;
using Flit.Modules.Security.Domain.Auth;
using Flit.Notificaciones.Api.Envio;
using Flit.Notificaciones.Api.Persistence;
using Flit.Notificaciones.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using EntregaProto = Flit.Notificaciones.Grpc.V1.Entrega;

namespace Flit.Notificaciones.Api.Grpc;

/// <summary>
/// <c>flit.notificaciones.v1.NotificacionesService</c> (HU #13353): envío directo de un correo ya armado (lo usa el buzón
/// de pruebas, que necesita la respuesta en el momento) y consulta de entregas de la empresa de la llamada.
/// </summary>
internal sealed class NotificacionesGrpcService(EnvioDeCorreo envio, NotificacionesDb db) : NotificacionesService.NotificacionesServiceBase
{
    public const string Scope = "platform.notificaciones.send";

    public override async Task<EnviarCorreoResponse> EnviarCorreo(EnviarCorreoRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var caller = PlatformServiceCaller.From(context);
        var correo = request.Correo ?? throw Invalido("Falta el correo.");
        if (string.IsNullOrWhiteSpace(correo.DestinatarioEmail) || string.IsNullOrWhiteSpace(correo.Plantilla))
            throw Invalido("El correo necesita destinatario y plantilla.");

        var pedido = new PedidoDeEnvio(
            CorreoMapeo.AMensaje(correo, caller.TenantId),
            CorreoMapeo.ACanal(correo.Canal),
            Origen(caller.ClientId),
            // El buzón de pruebas es el único que llama directo y su destinatario es un buzón controlado (HU #11372).
            BuzonControlado: true);
        var (resultado, entregaId) = await envio.EnviarAsync(pedido, context.CancellationToken).ConfigureAwait(false);
        return new EnviarCorreoResponse
        {
            Resultado = CorreoMapeo.AResultado(resultado.Outcome),
            Mensaje = resultado.Message,
            Desviado = resultado.RecipientDiverted,
            EntregaId = entregaId.ToString(),
        };
    }

    public override async Task<ListarEntregasResponse> ListarEntregas(ListarEntregasRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var tenantId = PlatformServiceCaller.From(context).TenantId;
        var tamano = request.TamanoPagina is > 0 and <= 200 ? request.TamanoPagina : 50;
        var pagina = Math.Max(1, request.Pagina);
        var consulta = db.Entregas.AsNoTracking().Where(e => e.TenantId == tenantId);
        var total = await consulta.CountAsync(context.CancellationToken).ConfigureAwait(false);
        var filas = await consulta.OrderByDescending(e => e.OcurridoEn).ThenByDescending(e => e.Id)
            .Skip((pagina - 1) * tamano).Take(tamano).ToListAsync(context.CancellationToken).ConfigureAwait(false);

        var respuesta = new ListarEntregasResponse { Total = total };
        foreach (var e in filas)
        {
            var entrega = new EntregaProto
            {
                Id = e.Id.ToString(),
                Plantilla = e.Plantilla,
                Canal = e.Canal == CanalCorreoCodigos.EmpresaApi ? Canal.EmpresaApi : Canal.FlitSmtp,
                Destinatario = e.Destinatario,
                Enviado = e.Resultado == "enviado",
                Resultado = System.Enum.TryParse<EmailSendOutcome>(e.Desenlace, out var o) ? CorreoMapeo.AResultado(o) : ResultadoEnvio.Unspecified,
                DuracionMs = e.DuracionMs,
                Desviado = e.Desviado,
                OcurridoEn = Timestamp.FromDateTimeOffset(e.OcurridoEn),
                Origen = e.Origen,
            };
            if (e.MotivoFallo is not null)
                entrega.MotivoFallo = e.MotivoFallo;
            respuesta.Entregas.Add(entrega);
        }

        return respuesta;
    }

    private static string Origen(string clientId) => clientId.StartsWith("svc-", StringComparison.Ordinal) ? clientId["svc-".Length..] : clientId;

    private static RpcException Invalido(string detalle) => new(new Status(StatusCode.InvalidArgument, detalle));
}

/// <summary>Traducción entre el contrato gRPC y los tipos del envío (la usan gRPC y el consumidor de trabajos).</summary>
internal static class CorreoMapeo
{
    public static EmailMessage AMensaje(Correo correo, Guid? tenantId) =>
        new(tenantId, correo.Plantilla, correo.DestinatarioEmail, correo.DestinatarioNombre, correo.Asunto, correo.Html)
        {
            Attachments = [.. correo.Adjuntos.Select(a => new EmailAttachment(a.Nombre, a.ContentType, a.Contenido.ToByteArray()))],
            BccEmails = [.. correo.CopiaOculta],
            ThemeKind = correo.HasTema ? correo.Tema : null,
            ThemeVersion = correo.HasTemaVersion ? correo.TemaVersion : null,
            SenderDisplayName = correo.HasRemitenteNombre ? correo.RemitenteNombre : null,
        };

    public static CanalCorreo ACanal(Canal canal) => canal == Canal.EmpresaApi ? CanalCorreo.EmpresaApi : CanalCorreo.FlitSmtp;

    public static ResultadoEnvio AResultado(EmailSendOutcome outcome) => outcome switch
    {
        EmailSendOutcome.Sent => ResultadoEnvio.Enviado,
        EmailSendOutcome.AuthenticationFailed => ResultadoEnvio.AutenticacionFallida,
        EmailSendOutcome.RecipientRejected => ResultadoEnvio.DestinatarioRechazado,
        EmailSendOutcome.ContentRejected => ResultadoEnvio.ContenidoRechazado,
        EmailSendOutcome.RateLimited => ResultadoEnvio.LimiteAlcanzado,
        EmailSendOutcome.ProviderUnavailable => ResultadoEnvio.ProveedorNoDisponible,
        EmailSendOutcome.TimedOut => ResultadoEnvio.TiempoAgotado,
        EmailSendOutcome.ConfigurationIncomplete => ResultadoEnvio.ConfiguracionIncompleta,
        _ => ResultadoEnvio.Unspecified,
    };
}
