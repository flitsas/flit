using Flit.Consultas.Grpc.V1;
using Grpc.Core;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>
/// RUNT real del vehículo (HU #13878, F10-C9, F10-C10): <c>RuntIntempoService</c> de core-consultas por
/// <see cref="ClienteRuntIntempo"/> (token de servicio, deadline y la empresa en <c>x-flit-tenant-id</c>). Elige el rpc por
/// <see cref="IdentificadorVehiculo.EsVin"/> (G2=b): <c>ConsultarPorVin</c> con un VIN y <c>ConsultarPorPlaca</c> en otro caso.
/// Toda respuesta OK pasa por <see cref="MapperIntempo.Mapear(ConsultarVehiculoIntempoResponse, string)"/> con
/// <see cref="Servicio"/> (el error del proveedor viaja dentro del mensaje y lo traduce el mapper). Errores gRPC (calcado de
/// <see cref="FuenteImpuestoReal"/>):
/// <list type="bullet">
/// <item><c>FAILED_PRECONDITION</c> (Intempo sin configurar en core-consultas) y <c>UNIMPLEMENTED</c> (core-consultas sin
/// F10-H4) → <see cref="TipoErrorFuente.NoConectado"/>, definitivo y sin reintentos. Se resuelve AQUÍ, no en
/// <see cref="TraductorErroresConsultas"/>, para no cambiar las demás fuentes.</item>
/// <item>El resto llega traducido por <see cref="TraductorErroresConsultas"/>: <c>Unavailable</c> → <c>RED</c> y
/// <c>DeadlineExceeded</c> → <c>TIMEOUT</c> (transitorios); <c>Unauthenticated</c>/<c>PermissionDenied</c> →
/// <c>AUTENTICACION</c>; <c>InvalidArgument</c> y los demás → <c>ESTADO_NO_OK</c> (definitivos).</item>
/// </list>
/// Los mensajes son fijos: nunca llevan la placa, el VIN, documentos ni el detalle del servidor. Sin logs propios.
/// </summary>
/// <example>
/// <code>
/// var r = await new FuenteRuntReal(cliente).ConsultarVehiculoAsync(empresa, new IdentificadorVehiculo(TipoIdentificador.PlacaCarro, "TST601"), ct);
/// </code>
/// </example>
public sealed class FuenteRuntReal(ClienteRuntIntempo cliente) : IFuenteRunt
{
    /// <summary>Servicio que resolvió (<c>EjecutorCeldaRunt</c> lo lleva a <c>ServicioConsulta.Intempo</c>).</summary>
    public const string Servicio = "INTEMPO";

    public async Task<ResultadoRunt> ConsultarVehiculoAsync(Guid empresa, IdentificadorVehiculo identificador, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(identificador);
        var valor = identificador.Valor.Trim();
        var respuesta = identificador.EsVin
            ? await cliente.LlamarAsync(empresa, (c, o) => c.ConsultarPorVinAsync(new ConsultarPorVinRequest { Vin = valor }, o), ct).ConfigureAwait(false)
            : await cliente.LlamarAsync(empresa, (c, o) => c.ConsultarPorPlacaAsync(new ConsultarPorPlacaRequest { Placa = valor }, o), ct).ConfigureAwait(false);
        if (respuesta.Respuesta is { } r)
            return MapperIntempo.Mapear(r, Servicio);

        if (respuesta.Codigo is StatusCode.FailedPrecondition or StatusCode.Unimplemented)
            return ResultadoRunt.Fallo(TipoErrorFuente.NoConectado, FuenteRuntNoConectada.Mensaje);

        var error = respuesta.Error!;
        return ResultadoRunt.Fallo(Tipo(error.TipoError), error.Mensaje);
    }

    /// <summary>Código de <see cref="TraductorErroresConsultas"/> → <see cref="TipoErrorFuente"/>; desconocido → definitivo.</summary>
    private static TipoErrorFuente Tipo(string codigo) =>
        Enum.GetValues<TipoErrorFuente>().FirstOrDefault(t => TipoErrorFuenteCodigos.Codigo(t) == codigo, TipoErrorFuente.EstadoNoOk);
}
