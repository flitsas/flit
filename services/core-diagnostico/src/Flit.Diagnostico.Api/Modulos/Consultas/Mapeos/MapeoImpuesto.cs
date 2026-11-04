using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Domain.Consultas;
using Grpc.Core;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Mapeos;

/// <summary>
/// Parte de respuesta del impuesto departamental (ADR-0075; antes en <c>FuenteImpuestoReal</c>, HU #13862, F09-C5, F09-C6):
/// normaliza <c>ConsultarImpuestoVehicular</c> con <see cref="ResultadoImpuesto.Normalizar"/> y el departamento consultado; el
/// proveedor solo va al servicio del intento (R-20). Errores: <c>UNIMPLEMENTED</c> (core-consultas sin C3) es
/// <c>NO_CONECTADO</c>, definitivo. Un chequeo <c>impuesto_*</c> conocido se traduce a su código; el resto de chequeos en ERROR
/// sigue por <see cref="TraductorErroresConsultas"/> (<c>HTTP_5XX</c>). <c>dato_parcial</c> no arma resultado.
/// </summary>
/// <example>
/// <code>
/// var r = MapeoImpuesto.Mapear(respuesta, "05"); // RespuestaFuente&lt;ResultadoImpuesto&gt;
/// </code>
/// </example>
public static class MapeoImpuesto
{
    /// <summary>Código de fuente en el mensaje de <c>NO_CONECTADO</c>.</summary>
    public const string CodigoFuente = "IMPUESTO_DEPARTAMENTAL";

    public static RespuestaFuente<ResultadoImpuesto> Mapear(ConsultarImpuestoVehicularResponse respuesta, string departamentoCodigo)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        var resultado = respuesta.Resultado;
        var servicio = TraductorErroresConsultas.Servicio(resultado?.Proveedor);
        if (ErrorDeChequeo(resultado) is { } deClave)
            return RespuestaFuente<ResultadoImpuesto>.Fallo(deClave, servicio);
        if (TraductorErroresConsultas.Traducir(resultado) is { } error)
            return RespuestaFuente<ResultadoImpuesto>.Fallo(error, servicio);

        return RespuestaFuente<ResultadoImpuesto>.Exito(ResultadoImpuesto.Normalizar(respuesta, departamentoCodigo), servicio, null);
    }

    /// <summary>
    /// La clave <c>impuesto_*</c> del chequeo en ERROR, si es un código conocido. El mensaje es fijo y no copia texto del proveedor.
    /// <c>dato_parcial</c> no cambia el error: el resultado no se arma y el monto no va al payload.
    /// </summary>
    private static ErrorConsulta? ErrorDeChequeo(ResultadoConsulta? resultado)
    {
        var caido = resultado?.Chequeos.FirstOrDefault(c => c.Estado == EstadoChequeo.Error);
        if (caido is null || !caido.Clave.StartsWith("impuesto_", StringComparison.Ordinal))
            return null;

        var codigo = caido.Clave["impuesto_".Length..].ToUpperInvariant() switch
        {
            "LIMITE" => "LIMITE_PROVEEDOR",
            "RECHAZO" => "ESTADO_NO_OK",
            "CAIDA" => "HTTP_5XX",
            var otro => otro,
        };
        return ClasificadorErrores.EsConocido(codigo)
            ? new ErrorConsulta(codigo, "El impuesto no quedó confirmado.")
            : null;
    }

    public static RespuestaFuente<ResultadoImpuesto> Error(StatusCode codigo) =>
        RespuestaFuente<ResultadoImpuesto>.Fallo(
            codigo == StatusCode.Unimplemented ? ErroresFuentePersona.NoConectada(CodigoFuente) : TraductorErroresConsultas.Traducir(codigo));
}
