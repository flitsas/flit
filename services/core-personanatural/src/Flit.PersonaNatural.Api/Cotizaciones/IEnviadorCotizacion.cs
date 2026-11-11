using Flit.Notificaciones.Grpc.V1;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;

namespace Flit.PersonaNatural.Api.Cotizaciones;

/// <summary>
/// Envía una cotización ya armada a un correo (HU #14043). El correo viaja solo hasta el transporte: ninguna
/// implementación lo guarda ni lo escribe en logs.
/// </summary>
public interface IEnviadorCotizacion
{
    /// <returns><c>true</c> si el transporte aceptó el correo.</returns>
    Task<bool> EnviarAsync(string correo, string asunto, string html, CancellationToken ct);
}

internal sealed class EnviadorCotizacionNoConfigurado : IEnviadorCotizacion
{
    public Task<bool> EnviarAsync(string correo, string asunto, string html, CancellationToken ct) =>
        throw new FachadaException("servicio_no_disponible", "El envío por correo no está disponible por ahora.", StatusCodes.Status503ServiceUnavailable);
}

/// <summary>
/// Por <c>NotificacionesService.EnviarCorreo</c> de core-notificaciones con el token de servicio del SDK, a nombre de la
/// empresa de Persona Natural y por el SMTP de FLIT. Pide el scope <c>platform.notificaciones.send</c>, que el cliente
/// <c>svc-personanatural</c> debe tener declarado en core-api (Suite:Oidc:ServiceClients).
/// </summary>
internal sealed partial class EnviadorCotizacionGrpc(NotificacionesService.NotificacionesServiceClient cliente, ILogger<EnviadorCotizacionGrpc> logger) : IEnviadorCotizacion
{
    public const string Scope = "platform.notificaciones.send";
    public const string Plantilla = "persona-natural.cotizacion";

    public async Task<bool> EnviarAsync(string correo, string asunto, string html, CancellationToken ct)
    {
        var request = new EnviarCorreoRequest
        {
            Correo = new Correo { Plantilla = Plantilla, Canal = Canal.FlitSmtp, DestinatarioEmail = correo, Asunto = asunto, Html = html },
        };
        var metadata = new Metadata { { PlatformServiceCallInterceptor.TenantMetadata, PlatformTenants.PersonaNatural.ToString() } };
        try
        {
            var r = await cliente.EnviarCorreoAsync(request, metadata, cancellationToken: ct).ResponseAsync.ConfigureAwait(false);
            return r.Resultado == ResultadoEnvio.Enviado;
        }
        catch (RpcException ex)
        {
            // Solo el estado: el mensaje de la excepción podría traer el destinatario.
            NoEnviado(logger, ex.StatusCode);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "core-notificaciones no aceptó la cotización (estado gRPC {Estado})")]
    private static partial void NoEnviado(ILogger logger, StatusCode estado);
}
