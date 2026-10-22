using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>
/// SIMIT simulado (HU #13793): solo con <c>Diagnostico:Simulacion:Habilitada=true</c> en LOCAL o DEV
/// (<see cref="SeleccionAdaptadores"/>). Determinista por el último carácter de la placa (o del número del documento en la
/// consulta por persona); datos inventados, sin infractor, y nada sale del servicio:
/// <list type="table">
/// <item><term>2</term><description>dos comparendos pendientes (semáforo ROJO)</description></item>
/// <item><term>3</term><description>un comparendo con acuerdo de pago (semáforo AMARILLO)</description></item>
/// <item><term>6</term><description>muchos comparendos pendientes (semáforo ROJO): la persona jurídica con flota de la prueba
/// de tamaño de SIMIT por persona (HU #13794)</description></item>
/// <item><term>7</term><description>error transitorio <c>TIMEOUT</c></description></item>
/// <item><term>8</term><description>error definitivo <c>RESPUESTA_INVALIDA</c></description></item>
/// <item><term>otro (1…)</term><description>sin comparendos (semáforo VERDE)</description></item>
/// </list>
/// </summary>
/// <example>
/// <code>
/// var r = await new FuenteSimitSimulada().ConsultarAsync(empresa, SolicitudSimit.PorPlaca("TST102"), ct); // dos comparendos
/// </code>
/// </example>
public sealed class FuenteSimitSimulada : IFuenteSimit
{
    public const string Transaccion = "SIM-SIMIT";

    private const string Organismo = "SECRETARIA DE TRANSITO DE PRUEBA";

    /// <summary>Comparendos del caso 6 (prueba de tamaño del payload por persona).</summary>
    public const int MuchosComparendos = 60;

    public Task<RespuestaFuente<ResultadoSimit>> ConsultarAsync(Guid empresa, SolicitudSimit solicitud, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var clave = solicitud.Placa ?? solicitud.Documento?.Numero ?? string.Empty;
        var respuesta = (clave.Length > 0 ? clave[^1] : '1') switch
        {
            '2' => Exito("ROJO",
                new ComparendoSimit("SIM-0001", "2026-01-10", "604100.00", "PENDIENTE", Organismo),
                new ComparendoSimit("SIM-0002", "2026-02-11", "302050.00", "PENDIENTE", Organismo)),
            '3' => Exito("AMARILLO", new ComparendoSimit("SIM-0003", "2025-11-03", "604100.00", "ACUERDO DE PAGO", Organismo)),
            '6' => Exito("ROJO", [.. Enumerable.Range(1, MuchosComparendos)
                .Select(i => new ComparendoSimit($"SIM-6{i:D3}", $"2026-{(i % 12) + 1:D2}-{(i % 28) + 1:D2}", "302050.00", "PENDIENTE", Organismo))]),
            '7' => RespuestaFuente<ResultadoSimit>.Fallo(new ErrorConsulta(ErroresFuentePersona.Timeout, "SIMIT simulado: tiempo de espera agotado."), ServicioConsulta.Smartcloud),
            '8' => RespuestaFuente<ResultadoSimit>.Fallo(new ErrorConsulta(ErroresFuentePersona.RespuestaInvalida, "SIMIT simulado: respuesta sin la forma esperada."), ServicioConsulta.Smartcloud),
            _ => Exito("VERDE"),
        };
        return Task.FromResult(respuesta);
    }

    private static RespuestaFuente<ResultadoSimit> Exito(string semaforo, params ComparendoSimit[] comparendos) =>
        RespuestaFuente<ResultadoSimit>.Exito(new ResultadoSimit(semaforo, comparendos.Length, comparendos), ServicioConsulta.Smartcloud, Transaccion);
}
