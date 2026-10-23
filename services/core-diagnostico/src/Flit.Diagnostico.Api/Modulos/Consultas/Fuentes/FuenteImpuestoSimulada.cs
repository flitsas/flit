using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>
/// Impuesto simulado (HU #13862): solo con <c>Diagnostico:Simulacion:Habilitada=true</c> en LOCAL o DEV
/// (<see cref="SeleccionAdaptadores"/>). Determinista por el último carácter de la placa; montos inventados, sin PII ni
/// proveedor, y nada sale del servicio (cubierto por <c>FixturesSinPiiTests</c>):
/// <list type="table">
/// <item><term>0</term><description>sin deuda: vigencias 2025 y 2026 pagadas, <c>monto_total</c> 0</description></item>
/// <item><term>7</term><description>error transitorio <c>TIMEOUT</c></description></item>
/// <item><term>otro</term><description>vigencias 2025 (210.000) y 2026 (190.000) sin pagar: <c>monto_total</c> 400.000</description></item>
/// </list>
/// El intento queda con <see cref="ServicioConsulta.Otro"/> y la transacción <see cref="Transaccion"/>.
/// </summary>
/// <example>
/// <code>
/// var r = await new FuenteImpuestoSimulada().ConsultarAsync(empresa, new SolicitudImpuesto("TST300", "05"), ct); // sin deuda
/// </code>
/// </example>
public sealed class FuenteImpuestoSimulada : IFuenteImpuesto
{
    public const string Transaccion = "SIM-IMPUESTO";

    public Task<RespuestaFuente<ResultadoImpuesto>> ConsultarAsync(Guid empresa, SolicitudImpuesto solicitud, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var placa = solicitud.Placa?.Trim() ?? string.Empty;
        var departamento = solicitud.DepartamentoCodigo.Trim();
        var respuesta = (placa.Length > 0 ? placa[^1] : '1') switch
        {
            '0' => Exito(departamento, new(2025, 180_000, true), new(2026, 175_000, true)),
            '7' => RespuestaFuente<ResultadoImpuesto>.Fallo(
                new ErrorConsulta(ErroresFuentePersona.Timeout, "Impuesto simulado: tiempo de espera agotado."), ServicioConsulta.Otro),
            _ => Exito(departamento, new(2025, 210_000, false), new(2026, 190_000, false)),
        };
        return Task.FromResult(respuesta);
    }

    private static RespuestaFuente<ResultadoImpuesto> Exito(string departamento, params VigenciaResultadoImpuesto[] vigencias) =>
        RespuestaFuente<ResultadoImpuesto>.Exito(
            new ResultadoImpuesto(vigencias.Where(v => !v.Pagada).Sum(v => v.Valor), vigencias, departamento), ServicioConsulta.Otro, Transaccion);
}
