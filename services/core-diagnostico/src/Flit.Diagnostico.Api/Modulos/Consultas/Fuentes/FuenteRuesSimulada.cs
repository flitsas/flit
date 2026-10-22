using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>
/// RUES simulado (HU #13792): solo con <c>Diagnostico:Simulacion:Habilitada=true</c> en LOCAL o DEV
/// (<see cref="SeleccionAdaptadores"/>). Determinista por el último dígito del NIT; datos inventados, sin razón social, y nada
/// sale del servicio:
/// <list type="table">
/// <item><term>6</term><description>matrícula cancelada</description></item>
/// <item><term>7</term><description>error transitorio <c>TIMEOUT</c></description></item>
/// <item><term>8</term><description>error definitivo <c>RESPUESTA_INVALIDA</c></description></item>
/// <item><term>otro (5…)</term><description>matrícula activa renovada el 2026-03-31</description></item>
/// </list>
/// </summary>
/// <example>
/// <code>
/// var r = await new FuenteRuesSimulada().ConsultarAsync(empresa, "999990306", ct); // cancelada
/// </code>
/// </example>
public sealed class FuenteRuesSimulada : IFuenteRues
{
    public const string Transaccion = "SIM-RUES";

    public Task<RespuestaFuente<ResultadoRues>> ConsultarAsync(Guid empresa, string nit, CancellationToken ct)
    {
        var respuesta = FuenteRuntPersonaSimulada.UltimoDigito(nit) switch
        {
            '6' => Exito(new ResultadoRues(true, "CANCELADA", "2024-03-31")),
            '7' => RespuestaFuente<ResultadoRues>.Fallo(new ErrorConsulta(ErroresFuentePersona.Timeout, "RUES simulado: tiempo de espera agotado."), ServicioConsulta.Verifik),
            '8' => RespuestaFuente<ResultadoRues>.Fallo(new ErrorConsulta(ErroresFuentePersona.RespuestaInvalida, "RUES simulado: respuesta sin la forma esperada."), ServicioConsulta.Verifik),
            _ => Exito(new ResultadoRues(true, "ACTIVA", "2026-03-31")),
        };
        return Task.FromResult(respuesta);
    }

    private static RespuestaFuente<ResultadoRues> Exito(ResultadoRues resultado) =>
        RespuestaFuente<ResultadoRues>.Exito(resultado, ServicioConsulta.Verifik, Transaccion);
}
