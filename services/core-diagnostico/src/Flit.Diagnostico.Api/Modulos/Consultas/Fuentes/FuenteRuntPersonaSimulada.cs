using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>
/// RUNT persona simulado (HU #13792): solo con <c>Diagnostico:Simulacion:Habilitada=true</c> en LOCAL o DEV
/// (<see cref="SeleccionAdaptadores"/>). Determinista por el último dígito del documento; datos inventados, sin nombres, y
/// nada sale del servicio:
/// <list type="table">
/// <item><term>2</term><description>inscrita, sin licencias</description></item>
/// <item><term>3</term><description>licencia suspendida</description></item>
/// <item><term>7</term><description>error transitorio <c>TIMEOUT</c></description></item>
/// <item><term>8</term><description>error definitivo <c>RESPUESTA_INVALIDA</c></description></item>
/// <item><term>otro (1…)</term><description>licencia B1 vigente</description></item>
/// </list>
/// </summary>
/// <example>
/// <code>
/// var r = await new FuenteRuntPersonaSimulada().ConsultarAsync(empresa, new DocumentoPersona("CC", "9999900302"), ct); // sin licencias
/// </code>
/// </example>
public sealed class FuenteRuntPersonaSimulada : IFuenteRuntPersona
{
    public const string Transaccion = "SIM-RUNT-PERSONA";

    public Task<RespuestaFuente<ResultadoPersonaRunt>> ConsultarAsync(Guid empresa, DocumentoPersona documento, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(documento);
        var respuesta = UltimoDigito(documento.Numero) switch
        {
            '2' => Exito(new ResultadoPersonaRunt(true, "ACTIVO", [])),
            '3' => Exito(new ResultadoPersonaRunt(true, "SUSPENDIDO", [new LicenciaConduccion("B1", "SUSPENDIDA", "2030-01-31")])),
            '7' => RespuestaFuente<ResultadoPersonaRunt>.Fallo(new ErrorConsulta(ErroresFuentePersona.Timeout, "RUNT persona simulado: tiempo de espera agotado."), ServicioConsulta.Kyverum),
            '8' => RespuestaFuente<ResultadoPersonaRunt>.Fallo(new ErrorConsulta(ErroresFuentePersona.RespuestaInvalida, "RUNT persona simulado: respuesta sin la forma esperada."), ServicioConsulta.Kyverum),
            _ => Exito(new ResultadoPersonaRunt(true, "ACTIVO", [new LicenciaConduccion("B1", "ACTIVA", "2030-01-31")])),
        };
        return Task.FromResult(respuesta);
    }

    internal static char UltimoDigito(string numero) => numero?.LastOrDefault(char.IsAsciiDigit) ?? '0';

    private static RespuestaFuente<ResultadoPersonaRunt> Exito(ResultadoPersonaRunt resultado) =>
        RespuestaFuente<ResultadoPersonaRunt>.Exito(resultado, ServicioConsulta.Kyverum, Transaccion);
}
