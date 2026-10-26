using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Consultas.Bus;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Modulos.Consultas.Mapeos;
using Flit.Platform.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// Las dos mitades que reemplazan a <c>FuenteImpuestoReal</c> (ADR-0075), sin bus: el <c>*Request</c> que arma Diagnóstico, la
/// respuesta de un cliente gRPC falso de core-consultas y el <see cref="MapeoImpuesto"/> real de la respuesta (o del código gRPC).
/// </summary>
/// <example>
/// <code>
/// var r = await new ImpuestoPorMapeo(new ImpuestoGrpcFalso()).ConsultarAsync(empresa, new SolicitudImpuesto("TST001", "05"), ct);
/// </code>
/// </example>
public sealed class ImpuestoPorMapeo(ConsultasService.ConsultasServiceClient grpc)
{
    public async Task<RespuestaFuente<ResultadoImpuesto>> ConsultarAsync(Guid empresa, SolicitudImpuesto solicitud, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var request = new ConsultarImpuestoVehicularRequest
        {
            Opciones = new OpcionesConsulta(),
            Placa = new Placa { Valor = solicitud.Placa },
            DepartamentoCodigo = solicitud.DepartamentoCodigo,
        };
        try
        {
            return MapeoImpuesto.Mapear(await grpc.ConsultarImpuestoVehicularAsync(request, Opciones(empresa, ct)).ResponseAsync, solicitud.DepartamentoCodigo);
        }
        catch (RpcException ex)
        {
            return MapeoImpuesto.Error(ex.StatusCode);
        }
    }

    internal static CallOptions Opciones(Guid empresa, CancellationToken ct) =>
        new(new Metadata { { PlatformServiceCallInterceptor.TenantMetadata, empresa.ToString() } }, cancellationToken: ct);
}

/// <summary>
/// Las dos mitades que reemplazan a <c>FuenteAvaluoReal</c> (ADR-0075), sin bus: <see cref="SolicitudesProto.Avaluos"/>, la
/// respuesta de un cliente gRPC falso de core-consultas y el <see cref="MapeoAvaluo"/> real.
/// </summary>
/// <example>
/// <code>
/// var r = await new AvaluoPorMapeo(new AvaluosGrpcFalso()).ConsultarAsync(empresa, new SolicitudAvaluo("TST201", null, 2020, null, null, null), ct);
/// </code>
/// </example>
public sealed class AvaluoPorMapeo(ConsultasService.ConsultasServiceClient grpc)
{
    public async Task<RespuestaFuente<ResultadoAvaluo>> ConsultarAsync(Guid empresa, SolicitudAvaluo solicitud, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        try
        {
            return MapeoAvaluo.Mapear(await grpc.ConsultarAvaluosAsync(SolicitudesProto.Avaluos(solicitud), ImpuestoPorMapeo.Opciones(empresa, ct)).ResponseAsync);
        }
        catch (RpcException ex)
        {
            return MapeoAvaluo.Error(ex.StatusCode);
        }
    }
}
