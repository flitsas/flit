using System.Diagnostics;
using Flit.Consultas.Api.Grpc;
using Flit.Consultas.Api.RuntIntempo;
using Flit.Consultas.Api.Simulacion;
using Flit.Consultas.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Google.Protobuf;
using Grpc.Core;

namespace Flit.Consultas.Api.Bus;

/// <summary>
/// Lo que dejó UNA ejecución de un trabajo: la <see cref="Respuesta"/> del rpc (OK, todavía sin minimizar) o el código gRPC
/// <see cref="Error"/> (ERROR). Un proveedor caído que el servicio ya informa dentro de su respuesta (chequeo en ERROR,
/// <c>RESULTADO_RUNT_INTEMPO_ERROR</c>) es OK: lo decide Diagnóstico con sus mapeos, como hoy.
/// </summary>
/// <remarks><see cref="MotivoFallo"/>: con OK y el proveedor caído, el <see cref="Flit.Tramites.Application.UseCases.Consultations.FalloProveedor.Codigo"/>
/// del último resultado del proveedor (<see cref="RegistroFallosProveedor"/>); null si respondió o no lo informa.</remarks>
internal sealed record EjecucionTrabajo(IMessage? Respuesta, StatusCode? Error, bool DesdeCache, TimeSpan Duracion, string? MotivoFallo = null);

/// <summary>
/// Ejecuta un trabajo del diagnóstico (ADR-0075, Q3 y Q4 con U2 = a): parsea la <see cref="TrabajoConsultaDiagnostico.Solicitud"/>
/// como el <c>*Request</c> de su rpc (proto-JSON estricto) y llama UNA sola vez al mismo método medido que atiende el gRPC, con el
/// llamador del sobre. Así se conservan la medición de consumo, los overrides por empresa (proveedor SIMIT con
/// <c>FinesProviderResolver</c>), la cadena de respaldo y la caché de proveedor. Nunca lanza por errores de la consulta:
/// <list type="bullet">
/// <item><see cref="RpcException"/> (entrada inválida, no conectado…) → su <see cref="StatusCode"/>.</item>
/// <item>Solicitud que no es del contrato → <see cref="StatusCode.InvalidArgument"/>; rpc desconocido, y el impuesto mientras no
/// exista su consulta real (C3) → <see cref="StatusCode.Unimplemented"/>, lo mismo que respondería el gRPC.</item>
/// <item>Pasado <see cref="OpcionesConsumidoresDiagnostico.TiempoMaximo"/> → <see cref="StatusCode.DeadlineExceeded"/>.</item>
/// <item>Cualquier otra excepción → <see cref="StatusCode.Internal"/> (definitiva).</item>
/// </list>
/// Con <see cref="OpcionesSimulacion.Habilitada"/> (solo LOCAL y DEV) responde <see cref="SimuladorConsultas"/>, medido con el
/// proveedor <see cref="SimuladorConsultas.Proveedor"/>; la validación de la solicitud y los códigos de error son los mismos.
/// Solo la cancelación del consumidor (el servicio se detiene) se propaga, para que el mensaje vuelva a la cola. Los logs llevan
/// el trabajo, el rpc, el código y el tipo de la excepción: nunca la solicitud, la respuesta ni el mensaje de la excepción.
/// </summary>
/// <example>
/// <code>
/// var ejecucion = await ejecutor.EjecutarAsync(trabajo, new PlatformServiceCaller("svc-diagnostico", empresa), ct);
/// </code>
/// </example>
internal sealed class EjecutorTrabajo(
    ConsultasGrpcService consultas,
    RuntIntempoGrpcService intempo,
    ConsumoRecorder consumo,
    OpcionesConsumidoresDiagnostico opciones,
    OpcionesSimulacion simulacion,
    SimuladorConsultas simulador,
    TimeProvider time,
    ILogger<EjecutorTrabajo> logger)
{
    /// <summary>Lector estricto: una clave que no es del <c>*Request</c> es un trabajo que no cumple el contrato.</summary>
    private static readonly JsonParser Lector = new(JsonParser.Settings.Default.WithIgnoreUnknownFields(false));

    public async Task<EjecucionTrabajo> EjecutarAsync(TrabajoConsultaDiagnostico trabajo, PlatformServiceCaller caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(trabajo);
        ArgumentNullException.ThrowIfNull(caller);
        var reloj = Stopwatch.StartNew();
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(opciones.TiempoMaximo);
        var deadline = time.GetUtcNow().UtcDateTime + opciones.TiempoMaximo;
        using var fallos = RegistroFallosProveedor.Abrir();
        try
        {
            var respuesta = await LlamarAsync(trabajo, caller, deadline, limite.Token).ConfigureAwait(false);
            return new EjecucionTrabajo(respuesta, null, DesdeCache(respuesta), reloj.Elapsed, RegistroFallosProveedor.Motivo());
        }
        catch (RpcException ex) when (ex.StatusCode != StatusCode.Cancelled || !ct.IsCancellationRequested)
        {
            return Fallo(trabajo, limite.IsCancellationRequested && !ct.IsCancellationRequested ? StatusCode.DeadlineExceeded : ex.StatusCode, reloj.Elapsed, null);
        }
        catch (Exception ex) when (ex is InvalidProtocolBufferException or InvalidJsonException)
        {
            return Fallo(trabajo, StatusCode.InvalidArgument, reloj.Elapsed, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Fallo(trabajo, StatusCode.DeadlineExceeded, reloj.Elapsed, null);
        }
#pragma warning disable CA1031 // Un trabajo nunca muere en silencio: la falla inesperada es un resultado ERROR (Q4).
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return Fallo(trabajo, StatusCode.Internal, reloj.Elapsed, ex.GetType().Name);
        }
    }

    private async Task<IMessage> LlamarAsync(TrabajoConsultaDiagnostico trabajo, PlatformServiceCaller caller, DateTime deadline, CancellationToken ct)
    {
        var solicitud = Solicitud(trabajo);

        // HU #13727 (U3 = a): en LOCAL y DEV responde el proveedor simulado, por el mismo camino (medición, minimización y
        // resultado), sin salir a ningún proveedor.
        if (simulacion.Habilitada)
        {
            return await consumo.MedirAsync(caller, trabajo.Fuente, () => Task.FromResult(simulador.Responder(trabajo.Rpc, solicitud)),
                _ => (SimuladorConsultas.Proveedor, SimuladorConsultas.Proveedor, false), ct).ConfigureAwait(false);
        }

        return solicitud switch
        {
            ConsultarPorPlacaRequest r => await intempo.ConsultarPorPlacaCore(r, caller, ct).ConfigureAwait(false),
            ConsultarPorVinRequest r => await intempo.ConsultarPorVinCore(r, caller, ct).ConfigureAwait(false),
            ConsultarConductorRequest r => await consultas.ConsultarConductor(r, caller, deadline, ct).ConfigureAwait(false),
            ConsultarMultasRequest r => await consultas.ConsultarMultas(r, caller, ct).ConfigureAwait(false),
            ConsultarRuesRequest r => await consultas.ConsultarRues(r, caller, ct).ConfigureAwait(false),
            ConsultarAvaluosRequest r => await consultas.ConsultarAvaluos(r, caller, ct).ConfigureAwait(false),
            // HU #13860: contrato primero; el servicio no lo implementa hasta C3 y por gRPC responde UNIMPLEMENTED. La solicitud
            // se validó igual, para que un trabajo mal formado sea INVALID_ARGUMENT y no se confunda con la falta de la consulta.
            _ => throw new RpcException(new Status(StatusCode.Unimplemented, "Rpc sin implementación en core-consultas.")),
        };
    }

    /// <summary>El <c>*Request</c> del rpc del trabajo; un rpc fuera del contrato es <see cref="StatusCode.Unimplemented"/>.</summary>
    private static IMessage Solicitud(TrabajoConsultaDiagnostico trabajo)
    {
        var json = trabajo.Solicitud.GetRawText();
        return trabajo.Rpc switch
        {
            RpcConsultas.PorPlaca => Lector.Parse<ConsultarPorPlacaRequest>(json),
            RpcConsultas.PorVin => Lector.Parse<ConsultarPorVinRequest>(json),
            RpcConsultas.Conductor => Lector.Parse<ConsultarConductorRequest>(json),
            RpcConsultas.Multas => Lector.Parse<ConsultarMultasRequest>(json),
            RpcConsultas.Rues => Lector.Parse<ConsultarRuesRequest>(json),
            RpcConsultas.Avaluos => Lector.Parse<ConsultarAvaluosRequest>(json),
            RpcConsultas.Impuesto => Lector.Parse<ConsultarImpuestoVehicularRequest>(json),
            _ => throw new RpcException(new Status(StatusCode.Unimplemented, "Rpc desconocido.")),
        };
    }

    private static bool DesdeCache(IMessage respuesta) => respuesta switch
    {
        ConsultarConductorResponse r => r.Resultado?.DesdeCache == true,
        ConsultarMultasResponse r => r.Resultado?.DesdeCache == true,
        ConsultarRuesResponse r => r.Resultado?.DesdeCache == true,
        ConsultarImpuestoVehicularResponse r => r.Resultado?.DesdeCache == true,
        _ => false,
    };

    private EjecucionTrabajo Fallo(TrabajoConsultaDiagnostico trabajo, StatusCode codigo, TimeSpan duracion, string? tipoExcepcion)
    {
        if (tipoExcepcion is null)
            EjecutorLog.Fallo(logger, trabajo.TrabajoId, trabajo.Rpc, codigo);
        else
            EjecutorLog.FallaInesperada(logger, trabajo.TrabajoId, trabajo.Rpc, tipoExcepcion);
        return new EjecucionTrabajo(null, codigo, false, duracion);
    }
}

internal static partial class EjecutorLog
{
    [LoggerMessage(EventId = 7421, Level = LogLevel.Information, Message = "Trabajo {TrabajoId} ({Rpc}) terminó en ERROR {Codigo}")]
    public static partial void Fallo(ILogger logger, Guid trabajoId, string rpc, StatusCode codigo);

    [LoggerMessage(EventId = 7422, Level = LogLevel.Error, Message = "Trabajo {TrabajoId} ({Rpc}): falla inesperada ({TipoExcepcion}); se publica ERROR Internal")]
    public static partial void FallaInesperada(ILogger logger, Guid trabajoId, string rpc, string tipoExcepcion);
}
