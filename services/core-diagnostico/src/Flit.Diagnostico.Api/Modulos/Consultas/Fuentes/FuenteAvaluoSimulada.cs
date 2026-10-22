using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>
/// Avalúo simulado (HU #13795): solo con <c>Diagnostico:Simulacion:Habilitada=true</c> en LOCAL o DEV
/// (<see cref="SeleccionAdaptadores"/>). Determinista por el último carácter de la placa (o del VIN si no hay placa); valores
/// inventados y nada sale del servicio:
/// <list type="table">
/// <item><term>1</term><description>las tres fuentes con valor (Fasecolda principal)</description></item>
/// <item><term>2</term><description>solo Fasecolda; MercadoLibre y base gravable sin datos</description></item>
/// <item><term>4</term><description>ninguna fuente con valor: <c>sin_valor_comercial</c></description></item>
/// <item><term>7</term><description>error transitorio <c>TIMEOUT</c></description></item>
/// <item><term>otro</term><description>las tres fuentes con valor</description></item>
/// </list>
/// </summary>
/// <example>
/// <code>
/// var r = await new FuenteAvaluoSimulada().ConsultarAsync(empresa, new SolicitudAvaluo("TST204", null, null, null, null, null), ct); // sin valor
/// </code>
/// </example>
public sealed class FuenteAvaluoSimulada : IFuenteAvaluo
{
    public const string Transaccion = "SIM-AVALUO";

    private const string Fasecolda = "fasecolda";
    private const string MercadoLibre = "mercado_libre";
    private const string BaseGravable = "base_gravable";

    public Task<RespuestaFuente<ResultadoAvaluo>> ConsultarAsync(Guid empresa, SolicitudAvaluo solicitud, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var clave = solicitud.Clave.Trim();
        var respuesta = (clave.Length > 0 ? clave[^1] : '1') switch
        {
            '2' => Exito(48_000_000, Fasecolda, Ok(Fasecolda, 48_000_000), SinDatos(MercadoLibre), SinDatos(BaseGravable)),
            '4' => Exito(null, null, SinDatos(Fasecolda), SinDatos(MercadoLibre), SinDatos(BaseGravable)),
            '7' => RespuestaFuente<ResultadoAvaluo>.Fallo(new ErrorConsulta(ErroresFuentePersona.Timeout, "Avalúo simulado: tiempo de espera agotado."), ServicioConsulta.Fasecolda),
            _ => Exito(52_000_000, Fasecolda, Ok(Fasecolda, 52_000_000), Ok(MercadoLibre, 50_500_000, 9), Ok(BaseGravable, 45_000_000)),
        };
        return Task.FromResult(respuesta);
    }

    private static ValorFuenteAvaluo Ok(string fuente, long valor, int? muestras = null) => new(fuente, ValorFuenteAvaluo.Ok, valor, muestras, null);

    private static ValorFuenteAvaluo SinDatos(string fuente) => new(fuente, ValorFuenteAvaluo.SinDatos, null, null, null);

    private static RespuestaFuente<ResultadoAvaluo> Exito(long? sugerido, string? principal, params ValorFuenteAvaluo[] fuentes) =>
        RespuestaFuente<ResultadoAvaluo>.Exito(
            new ResultadoAvaluo(sugerido is null, sugerido, principal, fuentes),
            principal is null ? ServicioConsulta.Fasecolda : TraductorErroresConsultas.Servicio(principal), Transaccion);
}
