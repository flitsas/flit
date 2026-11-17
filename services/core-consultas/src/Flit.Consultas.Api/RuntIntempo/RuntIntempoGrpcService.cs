using Flit.Consultas.Api.Grpc;
using Flit.Consultas.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;

namespace Flit.Consultas.Api.RuntIntempo;

/// <summary>
/// <c>flit.consultas.v1.RuntIntempoService</c> (HU #13876, F10-H4): el RUNT real por Intempo, calcado de
/// <c>Comparendos/FuentesComparendosGrpcService</c>. Dos rpcs (G2=b) con la misma respuesta: se valida la entrada
/// (vacía → <c>INVALID_ARGUMENT</c>), sin la URL base se responde <c>FAILED_PRECONDITION</c> con el nombre de la
/// variable, y si no se consulta y se mide el consumo (fuente <c>runt_intempo_placa</c> o <c>runt_intempo_vin</c>).
/// Sin caché y sin logs: la respuesta trae datos personales (Q1=b). Los métodos gRPC son envoltorios de los <c>*Core</c>, que
/// el consumidor del bus del diagnóstico (ADR-0075) llama con el llamador del sobre.
/// </summary>
internal sealed class RuntIntempoGrpcService(ProveedorRuntIntempo proveedor, ConsumoRecorder consumo)
    : RuntIntempoService.RuntIntempoServiceBase
{
    public const string FuentePlaca = "runt_intempo_placa";
    public const string FuenteVin = "runt_intempo_vin";

    public override Task<ConsultarVehiculoIntempoResponse> ConsultarPorPlaca(ConsultarPorPlacaRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ConsultarPorPlacaCore(request, PlatformServiceCaller.From(context), context.CancellationToken);
    }

    public override Task<ConsultarVehiculoIntempoResponse> ConsultarPorVin(ConsultarPorVinRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ConsultarPorVinCore(request, PlatformServiceCaller.From(context), context.CancellationToken);
    }

    /// <summary>Por placa con el llamador dado: valida, exige Intempo conectado y mide.</summary>
    internal Task<ConsultarVehiculoIntempoResponse> ConsultarPorPlacaCore(ConsultarPorPlacaRequest request, PlatformServiceCaller caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Placa))
            throw Invalido("Se necesita la placa.");
        Conectado();
        return consumo.MedirAsync(caller, FuentePlaca, () => proveedor.ConsultarPorPlacaAsync(request.Placa, ct), Resumen, ct);
    }

    /// <summary>Por VIN con el llamador dado: valida, exige Intempo conectado y mide.</summary>
    internal Task<ConsultarVehiculoIntempoResponse> ConsultarPorVinCore(ConsultarPorVinRequest request, PlatformServiceCaller caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Vin))
            throw Invalido("Se necesita el VIN.");
        Conectado();
        return consumo.MedirAsync(caller, FuenteVin, () => proveedor.ConsultarPorVinAsync(request.Vin, ct), Resumen, ct);
    }

    private void Conectado()
    {
        if (proveedor.NoConectado is { } motivo)
            throw new RpcException(new Status(StatusCode.FailedPrecondition, motivo));
    }

    private static (string Proveedor, string Resultado, bool DesdeCache) Resumen(ConsultarVehiculoIntempoResponse r) =>
        (r.Proveedor, r.Resultado switch
        {
            ResultadoRuntIntempo.Encontrado => "con_datos",
            ResultadoRuntIntempo.NoEncontrado => "vacia",
            _ => "error",
        }, false);

    private static RpcException Invalido(string mensaje) => new(new Status(StatusCode.InvalidArgument, mensaje));
}
