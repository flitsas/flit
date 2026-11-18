using System.Globalization;
using Flit.PersonaNatural.Api.Solicitudes;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using GrpcStatusCode = Grpc.Core.StatusCode;

namespace Flit.PersonaNatural.Api.Caja;

/// <summary>
/// Vocabulario de errores de la caja y su traducción a gRPC. El <c>reason</c> de <c>ErrorInfo</c> es el mismo código estable que
/// core-api devuelve al front (<c>{ code, message }</c>); los datos extra (<c>totalActualCop</c>, <c>retryAfterSegundos</c>) viajan en
/// <c>ErrorInfo.metadata</c>.
/// </summary>
public static class CajaErrores
{
    public const string Dominio = "flitsas.com";

    /// <summary>Los códigos del pago compartido (<see cref="Pagos.IRegistroDePagos"/>) a los de la tabla de errores de la caja.</summary>
    internal static (string Codigo, string Mensaje) Traducir(ReglaDeNegocioException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        return ex.Codigo switch
        {
            "vencido" => (CajaBusquedaService.CodigoCobroVencido, "El pago en caja de este trámite venció."),
            "estado_no_permite_pago" or "no_habilitado" or "medio_no_disponible" or "total_cero" or "no_es_canal_punto" =>
                (CajaBusquedaService.CodigoNoHabilitado, "Este trámite no tiene el pago en caja habilitado."),
            "medio_caja_invalido" => (CajaConfirmacionService.CodigoMedioInvalido, ex.Message),
            "solicitud_no_encontrada" => (CajaBusquedaService.CodigoNoEncontrado, "No encontramos ese trámite."),
            CajaBusquedaService.CodigoYaPagado => (CajaBusquedaService.CodigoYaPagado, "Este trámite ya fue pagado."),
            CajaBusquedaService.CodigoOtroOrganismo => (CajaBusquedaService.CodigoOtroOrganismo, "Este trámite no es de este organismo."),
            _ => (ex.Codigo, ex.Message),
        };
    }

    /// <summary>Estado gRPC de un estado HTTP de la regla de negocio.</summary>
    internal static GrpcStatusCode EstadoGrpc(int http) => http switch
    {
        400 or 413 or 422 => GrpcStatusCode.InvalidArgument,
        401 => GrpcStatusCode.Unauthenticated,
        403 => GrpcStatusCode.PermissionDenied,
        404 => GrpcStatusCode.NotFound,
        409 => GrpcStatusCode.FailedPrecondition,
        429 => GrpcStatusCode.ResourceExhausted,
        502 or 503 => GrpcStatusCode.Unavailable,
        _ => GrpcStatusCode.Internal,
    };

    /// <summary>La regla de negocio como <see cref="RpcException"/> con <c>reason</c> estable y los extras en <c>metadata</c>.</summary>
    internal static RpcException ARpc(ReglaDeNegocioException ex)
    {
        var (codigo, mensaje) = Traducir(ex);
        var info = new ErrorInfo { Reason = codigo, Domain = Dominio };
        if (ex.Extensiones is not null)
        {
            foreach (var (clave, valor) in ex.Extensiones.Where(e => e.Key != "errors" && e.Value is not null))
                info.Metadata[clave] = Convert.ToString(valor, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        var estado = new Google.Rpc.Status { Code = (int)EstadoGrpc(ex.Estado), Message = mensaje, Details = { Any.Pack(info) } };
        return estado.ToRpcException();
    }
}
