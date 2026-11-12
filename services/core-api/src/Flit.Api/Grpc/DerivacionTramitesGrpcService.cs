using System.Diagnostics;
using Flit.Api.Authorization;
using Flit.Api.Derivacion;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Grpc.V1;
using Grpc.Core;

namespace Flit.Api.Grpc;

/// <summary>
/// Adaptador gRPC del rpc de creación de borradores desde Diagnóstico (ADR-0084, diseño §3). Delgado: verifica el llamante
/// (<see cref="DiagnosticoDerivacionServicioAuthorization.VerificarLlamante"/>), toma un permiso del
/// <see cref="LimitadorDerivacion"/> (sin permiso: <c>RESOURCE_EXHAUSTED</c> inmediato) y delega en
/// <see cref="CreadorBorradorDiagnostico"/>. Habeas Data: los logs llevan ids (referencia = id del vehículo, intento),
/// código de resultado y duración; nunca placa, VIN ni documentos.
/// </summary>
internal sealed class DerivacionTramitesGrpcService(
    CreadorBorradorDiagnostico creador, LimitadorDerivacion limitador, ILogger<DerivacionTramitesGrpcService> logger)
    : DerivacionTramitesService.DerivacionTramitesServiceBase
{
    public override async Task<CrearBorradorDesdeDiagnosticoResponse> CrearBorradorDesdeDiagnostico(
        CrearBorradorDesdeDiagnosticoRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var caller = Verificar(context);

        using var permiso = limitador.Intentar();
        if (!permiso.IsAcquired)
        {
            DerivacionLog.Limitada(logger, caller.TenantId, limitador.Permisos);
            throw new RpcException(new Status(StatusCode.ResourceExhausted, "core-api está atendiendo el máximo de creaciones simultáneas; reintente con espera."));
        }

        var reloj = Stopwatch.StartNew();
        var respuesta = await creador.CrearAsync(caller.TenantId, request, context.CancellationToken).ConfigureAwait(false);
        var resultado = respuesta.ResultadoCase == CrearBorradorDesdeDiagnosticoResponse.ResultadoOneofCase.Creado
            ? respuesta.Creado.YaExistia ? "ya_existia" : "creado"
            : respuesta.Rechazo.Codigo;
        var referencia = Id(request.ExternalRef);
        var intento = Id(request.IntentoId);
        DerivacionLog.Resuelta(logger, caller.TenantId, referencia, intento, resultado, reloj.ElapsedMilliseconds);
        return respuesta;
    }

    public override async Task<ObtenerTramitesPorReferenciaResponse> ObtenerTramitesPorReferencia(
        ObtenerTramitesPorReferenciaRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var caller = Verificar(context);
        if (request.ExternalRefs.Count > CreadorBorradorDiagnostico.MaxReferencias)
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"Máximo {CreadorBorradorDiagnostico.MaxReferencias} referencias por llamada."));

        return await creador.PorReferenciasAsync(caller.TenantId, request.ExternalRefs, context.CancellationToken).ConfigureAwait(false);
    }

    private static PlatformServiceCaller Verificar(ServerCallContext context)
    {
        var caller = PlatformServiceCaller.From(context);
        DiagnosticoDerivacionServicioAuthorization.VerificarLlamante(caller);
        return caller;
    }

    /// <summary>Solo un uuid válido llega al log (la referencia y el intento son ids); cualquier otra cosa se omite.</summary>
    private static Guid? Id(string? valor) => Guid.TryParse(valor, out var id) ? id : null;
}

internal static partial class DerivacionLog
{
    [LoggerMessage(EventId = 84001, Level = LogLevel.Information, Message = "Derivación desde Diagnóstico: empresa {TenantId}, referencia {Referencia}, intento {Intento} resuelto como {Resultado} en {DuracionMs} ms.")]
    public static partial void Resuelta(ILogger logger, Guid tenantId, Guid? referencia, Guid? intento, string resultado, long duracionMs);

    [LoggerMessage(EventId = 84002, Level = LogLevel.Warning, Message = "Derivación desde Diagnóstico: limitador lleno ({Permisos} en curso) para la empresa {TenantId}; responde RESOURCE_EXHAUSTED.")]
    public static partial void Limitada(ILogger logger, Guid tenantId, int permisos);
}
