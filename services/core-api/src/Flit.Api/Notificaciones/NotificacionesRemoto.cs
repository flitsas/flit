using Flit.Admin.Application.Companies.NotificationDeliveryLogs;
using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Notifications.Admin;
using Flit.Modules.Notificaciones;
using Flit.Modules.Security.Domain.Auth;
using Flit.Notificaciones.Grpc.V1;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;

namespace Flit.Api.Notificaciones;

/// <summary>
/// core-api frente a core-notificaciones (Epic #13316, Feature #13324). Desde el corte (HU #13359) core-api no tiene
/// transportes de correo: los correos y webhooks se dejan como trabajos por el bus, y lo que necesita respuesta en el
/// momento va por gRPC como svc-tramites — mensajes muertos (HU #13357, scope <c>platform.notificaciones.admin</c>),
/// canales y buzón de pruebas, y el registro de entregas (scope <c>platform.notificaciones.send</c>).
/// <see cref="AddressKey"/> es obligatoria.
/// </summary>
internal static class NotificacionesRemoto
{
    public const string AddressKey = "Notificaciones:Remoto:Address";

    public static IServiceCollection AddNotificacionesRemoto(this IServiceCollection services, IConfiguration configuration)
    {
        if (!Uri.TryCreate(configuration[AddressKey], UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"{AddressKey} no es una URL (p. ej. http://core-notificaciones:8085): core-api envía por core-notificaciones.");

        services.AddFlitGrpcClient<MensajesMuertosService.MensajesMuertosServiceClient>(configuration, uri, "platform.notificaciones.admin");
        services.AddFlitGrpcClient<NotificacionesService.NotificacionesServiceClient>(configuration, uri, "platform.notificaciones.send");
        services.AddScoped<ICanalesDeNotificaciones, CanalesPorNotificaciones>();
        services.AddScoped<INotificationDeliveryLogRepository, EntregasPorNotificaciones>();
        return services;
    }

    /// <summary>Metadata con la empresa del envío (o la de la plataforma).</summary>
    internal static Metadata Empresa(Guid? tenantId) =>
        new() { { PlatformServiceCallInterceptor.TenantMetadata, PlatformTenants.O(tenantId).ToString() } };
}

/// <summary>Canales y buzón de pruebas por <c>NotificacionesService</c> (HU #13359).</summary>
internal sealed class CanalesPorNotificaciones(NotificacionesService.NotificacionesServiceClient client) : ICanalesDeNotificaciones
{
    public async Task<IReadOnlyList<CanalDeNotificacion>> ListarAsync(CancellationToken ct)
    {
        try
        {
            var r = await client.ListarCanalesAsync(new ListarCanalesRequest(), NotificacionesRemoto.Empresa(null), cancellationToken: ct).ConfigureAwait(false);
            return [.. r.Canales.Select(c => new CanalDeNotificacion(
                c.Canal == Canal.EmpresaApi ? NotificationChannel.TenantApi : NotificationChannel.FlitSmtp,
                c.Disponible,
                c.HasRemitenteEmail ? c.RemitenteEmail : null,
                c.HasRemitenteNombre ? c.RemitenteNombre : null,
                c.Consola))];
        }
        catch (Exception ex) when (ex is RpcException or InvalidOperationException && !ct.IsCancellationRequested)
        {
            throw new NotificacionesNoDisponibleException("Notificaciones no respondió.", ex);
        }
    }

    public async Task<EmailSendResult> EnviarPruebaAsync(NotificationChannel canal, EmailMessage mensaje, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mensaje);
        var correo = new Correo
        {
            Plantilla = mensaje.TemplateKey,
            Canal = canal == NotificationChannel.TenantApi ? Canal.EmpresaApi : Canal.FlitSmtp,
            DestinatarioEmail = mensaje.ToEmail,
            DestinatarioNombre = mensaje.ToName,
            Asunto = mensaje.Subject,
            Html = mensaje.HtmlBody,
        };
        correo.CopiaOculta.AddRange(mensaje.BccEmails);
        foreach (var a in mensaje.Attachments)
            correo.Adjuntos.Add(new Adjunto { Nombre = a.FileName, ContentType = a.ContentType, Contenido = Google.Protobuf.ByteString.CopyFrom(a.Content) });
        if (mensaje.SenderDisplayName is not null)
            correo.RemitenteNombre = mensaje.SenderDisplayName;
        if (mensaje.ThemeKind is not null)
            correo.Tema = mensaje.ThemeKind;
        if (mensaje.ThemeVersion is { } version)
            correo.TemaVersion = version;

        try
        {
            var r = await client.EnviarCorreoAsync(new EnviarCorreoRequest { Correo = correo }, NotificacionesRemoto.Empresa(mensaje.TenantId), cancellationToken: ct)
                .ConfigureAwait(false);
            var outcome = Desenlace(r.Resultado);
            var resultado = outcome == EmailSendOutcome.Sent
                ? new EmailSendResult(true, EmailSendOutcome.Sent, string.IsNullOrEmpty(r.Mensaje) ? "Enviado." : r.Mensaje)
                : new EmailSendResult(false, outcome, r.Mensaje);
            return resultado with { RecipientDiverted = r.Desviado, Channel = CanalCorreoCodigos.De(canal == NotificationChannel.TenantApi ? CanalCorreo.EmpresaApi : CanalCorreo.FlitSmtp) };
        }
        catch (Exception ex) when (ex is RpcException or InvalidOperationException && !ct.IsCancellationRequested)
        {
            return new EmailSendResult(false, EmailSendOutcome.ProviderUnavailable, "El servicio de notificaciones no respondió.");
        }
    }

    private static EmailSendOutcome Desenlace(ResultadoEnvio r) => r switch
    {
        ResultadoEnvio.Enviado => EmailSendOutcome.Sent,
        ResultadoEnvio.AutenticacionFallida => EmailSendOutcome.AuthenticationFailed,
        ResultadoEnvio.DestinatarioRechazado => EmailSendOutcome.RecipientRejected,
        ResultadoEnvio.ContenidoRechazado => EmailSendOutcome.ContentRejected,
        ResultadoEnvio.LimiteAlcanzado => EmailSendOutcome.RateLimited,
        ResultadoEnvio.TiempoAgotado => EmailSendOutcome.TimedOut,
        ResultadoEnvio.ConfiguracionIncompleta => EmailSendOutcome.ConfigurationIncomplete,
        _ => EmailSendOutcome.ProviderUnavailable,
    };
}

/// <summary>
/// Registro de entregas de una empresa (HU #11363) leído de <c>notificaciones.entregas</c> con <c>ListarEntregas</c>
/// (HU #13359): desde el corte core-api ya no escribe <c>admin.notification_delivery_logs</c>.
/// </summary>
internal sealed class EntregasPorNotificaciones(NotificacionesService.NotificacionesServiceClient client) : INotificationDeliveryLogRepository
{
    public async Task<IReadOnlyList<NotificationDeliveryLogRecord>> ListByTenantAsync(Guid tenantId, int skip, int take, CancellationToken cancellationToken = default)
    {
        // ListarEntregas pagina por página: con un salto que no cae en página se pide desde el principio y se recorta.
        var alineado = take > 0 && skip % take == 0;
        var pedido = alineado
            ? new ListarEntregasRequest { TamanoPagina = take, Pagina = (skip / take) + 1 }
            : new ListarEntregasRequest { TamanoPagina = Math.Min(skip + take, 200), Pagina = 1 };
        var r = await client.ListarEntregasAsync(pedido, NotificacionesRemoto.Empresa(tenantId), cancellationToken: cancellationToken).ConfigureAwait(false);
        var filas = r.Entregas.Select(e => new NotificationDeliveryLogRecord(
            Guid.TryParse(e.Id, out var id) ? id : Guid.Empty,
            e.Plantilla,
            e.Canal == Canal.EmpresaApi ? CanalCorreoCodigos.EmpresaApi : CanalCorreoCodigos.FlitSmtp,
            e.Destinatario,
            e.Enviado ? "enviado" : "fallido",
            e.HasMotivoFallo ? e.MotivoFallo : null,
            e.DuracionMs,
            e.OcurridoEn.ToDateTimeOffset()));
        return alineado ? [.. filas] : [.. filas.Skip(skip).Take(take)];
    }
}
