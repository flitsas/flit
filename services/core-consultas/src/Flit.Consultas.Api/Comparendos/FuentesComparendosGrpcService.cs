using Flit.Consultas.Api.Grpc;
using Flit.Consultas.Grpc.V1;
using Grpc.Core;

namespace Flit.Consultas.Api.Comparendos;

/// <summary>
/// <c>flit.consultas.v1.FuentesComparendosService</c> (HU #13520): las fuentes de Gestión de Comparendos. Quien llama
/// elige canal y proveedor; aquí se valida, se ejecuta y se mide el consumo (fuente «comparendos»). Separado de
/// <c>ConsultarMultas</c> a propósito (decisión del 2026-10-13).
/// </summary>
internal sealed class FuentesComparendosGrpcService(IEnumerable<IProveedorComparendos> proveedores, ProveedorContratosRenting contratos, ConsumoRecorder consumo)
    : FuentesComparendosService.FuentesComparendosServiceBase
{
    public override Task<ListarProveedoresComparendosResponse> ListarProveedoresComparendos(ListarProveedoresComparendosRequest request, ServerCallContext context)
    {
        var r = new ListarProveedoresComparendosResponse();
        foreach (var p in proveedores)
        {
            var item = new ProveedorComparendos { Clave = p.Clave, Nombre = p.Nombre, Conectado = p.NoConectado is null };
            item.Canales.AddRange(p.Canales);
            if (p.NoConectado is { } motivo)
                item.Motivo = motivo;
            r.Proveedores.Add(item);
        }

        return Task.FromResult(r);
    }

    public override Task<ConsultarComparendosResponse> ConsultarComparendos(ConsultarComparendosRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Canal == CanalComparendos.Unspecified)
            throw Invalido("Indica el canal.");
        if (Fuente.PorPlaca(request.Canal) ? string.IsNullOrWhiteSpace(request.Placa) : string.IsNullOrWhiteSpace(request.NumeroDocumento))
            throw Invalido(Fuente.PorPlaca(request.Canal) ? "Se necesita la placa." : "Se necesita el número de documento.");
        var proveedor = proveedores.FirstOrDefault(p => p.Clave == request.Proveedor)
            ?? throw new RpcException(new Status(StatusCode.FailedPrecondition, $"Proveedor desconocido: «{request.Proveedor}»."));
        if (!proveedor.Canales.Contains(request.Canal))
            throw new RpcException(new Status(StatusCode.FailedPrecondition, $"{proveedor.Nombre} no atiende el canal {request.Canal}."));
        if (proveedor.NoConectado is { } motivo)
            throw new RpcException(new Status(StatusCode.FailedPrecondition, motivo));

        return consumo.MedirAsync(context, "comparendos", () => proveedor.ConsultarAsync(request, context.CancellationToken), r =>
            (r.Proveedor, r.Parciales.Any(p => p.Resultado == ResultadoFuente.Error) ? "error"
                : r.Parciales.All(p => p.Resultado == ResultadoFuente.ExitosaVacia) ? "vacia" : "con_datos", false));
    }

    public override Task<ConsultarContratoConductorResponse> ConsultarContratoConductor(ConsultarContratoConductorRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Proveedor != ProveedorContratosRenting.ClaveProveedor)
            throw new RpcException(new Status(StatusCode.FailedPrecondition, $"Proveedor de contratos desconocido: «{request.Proveedor}»."));
        if (string.IsNullOrWhiteSpace(request.Placa))
            throw Invalido("Se necesita la placa.");
        if (!DateOnly.TryParseExact(request.Fecha, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var fecha))
            throw Invalido("Se necesita la fecha (AAAA-MM-DD).");
        if (contratos.NoConectado is { } motivo)
            throw new RpcException(new Status(StatusCode.FailedPrecondition, motivo));

        return consumo.MedirAsync(context, "contratos", () => contratos.ConsultarAsync(request.Placa, fecha, context.CancellationToken), r =>
            (r.Proveedor, r.Resultado switch
            {
                ResultadoContratoConductor.Encontrado => "con_datos",
                ResultadoContratoConductor.NoEncontrado => "vacia",
                _ => "error",
            }, false));
    }

    private static RpcException Invalido(string mensaje) => new(new Status(StatusCode.InvalidArgument, mensaje));
}
