using Flit.Comparendos.Domain.Modelo;
using Flit.Notificaciones.Grpc.V1;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Grpc;
using Google.Protobuf;
using Grpc.Core;

namespace Flit.Comparendos.Api.Modulos.Correo;

/// <summary>Lo que se le entrega al transporte (un destinatario, con adjuntos ya leídos).</summary>
public sealed record CorreoParaEnviar(
    string Plantilla,
    CanalCorreo Canal,
    string DestinatarioEmail,
    string? DestinatarioNombre,
    string Asunto,
    string Html,
    IReadOnlyList<AdjuntoCorreo> Adjuntos,
    IReadOnlyList<string> CopiaOculta,
    string? RemitenteNombre);

/// <summary>Resultado del transporte, con los mismos nombres que <c>ResultadoEnvio</c> de Notificaciones.</summary>
public sealed record ResultadoEnvioCorreo(string Resultado, string? Mensaje, bool Desviado, string? EntregaId)
{
    public const string Enviado = "ENVIADO";

    public bool Exito => Resultado == Enviado;

    /// <summary>Tiempo agotado, proveedor no disponible o límite alcanzado: se reintenta (§6.14).</summary>
    public bool Transitorio => Resultado is "TIEMPO_AGOTADO" or "PROVEEDOR_NO_DISPONIBLE" or "LIMITE_ALCANZADO";

    public bool Rebote => Resultado == "DESTINATARIO_RECHAZADO";
}

public interface IEnvioCorreo
{
    Task<ResultadoEnvioCorreo> EnviarAsync(Guid? tenantId, CorreoParaEnviar correo, CancellationToken ct);
}

/// <summary>Envío por <c>core-notificaciones</c> (gRPC <c>EnviarCorreo</c>, scope platform.notificaciones.send).</summary>
internal sealed class EnvioPorNotificaciones(NotificacionesService.NotificacionesServiceClient cliente) : IEnvioCorreo
{
    public async Task<ResultadoEnvioCorreo> EnviarAsync(Guid? tenantId, CorreoParaEnviar correo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(correo);
        var mensaje = new Flit.Notificaciones.Grpc.V1.Correo
        {
            Plantilla = correo.Plantilla,
            Canal = correo.Canal == CanalCorreo.EmpresaApi ? Canal.EmpresaApi : Canal.FlitSmtp,
            DestinatarioEmail = correo.DestinatarioEmail,
            DestinatarioNombre = correo.DestinatarioNombre ?? string.Empty,
            Asunto = correo.Asunto,
            Html = correo.Html,
        };
        if (correo.RemitenteNombre is { Length: > 0 } remitente)
            mensaje.RemitenteNombre = remitente;
        mensaje.CopiaOculta.AddRange(correo.CopiaOculta);
        mensaje.Adjuntos.AddRange(correo.Adjuntos.Select(a => new Adjunto { Nombre = a.Nombre, ContentType = a.ContentType, Contenido = ByteString.CopyFrom(a.Contenido) }));

        try
        {
            var respuesta = await cliente.EnviarCorreoAsync(new EnviarCorreoRequest { Correo = mensaje },
                new Metadata { { PlatformServiceCallInterceptor.TenantMetadata, PlatformTenants.O(tenantId).ToString() } }, cancellationToken: ct).ConfigureAwait(false);
            return new ResultadoEnvioCorreo(ResultadoTexto(respuesta.Resultado), respuesta.Mensaje, respuesta.Desviado, respuesta.EntregaId);
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded or StatusCode.Internal or StatusCode.ResourceExhausted)
        {
            return new ResultadoEnvioCorreo("PROVEEDOR_NO_DISPONIBLE", $"Notificaciones no respondió: {ex.Status.Detail}", false, null);
        }
    }

    private static string ResultadoTexto(ResultadoEnvio r) => r switch
    {
        ResultadoEnvio.Enviado => "ENVIADO",
        ResultadoEnvio.AutenticacionFallida => "AUTENTICACION_FALLIDA",
        ResultadoEnvio.DestinatarioRechazado => "DESTINATARIO_RECHAZADO",
        ResultadoEnvio.ContenidoRechazado => "CONTENIDO_RECHAZADO",
        ResultadoEnvio.LimiteAlcanzado => "LIMITE_ALCANZADO",
        ResultadoEnvio.ProveedorNoDisponible => "PROVEEDOR_NO_DISPONIBLE",
        ResultadoEnvio.TiempoAgotado => "TIEMPO_AGOTADO",
        ResultadoEnvio.ConfiguracionIncompleta => "CONFIGURACION_INCOMPLETA",
        _ => "PROVEEDOR_NO_DISPONIBLE",
    };
}

/// <summary>Local sin Notificaciones: el correo se escribe en el log y se da por enviado.</summary>
internal sealed partial class EnvioConsola(ILogger<EnvioConsola> logger) : IEnvioCorreo
{
    public Task<ResultadoEnvioCorreo> EnviarAsync(Guid? tenantId, CorreoParaEnviar correo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(correo);
        LogCorreo(logger, correo.Plantilla, correo.DestinatarioEmail, correo.Asunto, correo.Adjuntos.Count);
        return Task.FromResult(new ResultadoEnvioCorreo(ResultadoEnvioCorreo.Enviado, "Escrito en el log (sin Notificaciones).", false, $"consola-{Guid.NewGuid():N}"));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Correo {Plantilla} a {Destinatario}: «{Asunto}» ({Adjuntos} adjuntos).")]
    private static partial void LogCorreo(ILogger logger, string plantilla, string destinatario, string asunto, int adjuntos);
}
