namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>
/// Qué se avalúa (HU #13795, F05-C7): la placa definitiva y el VIN que dejó el RUNT (al menos uno) y los datos técnicos del
/// bien que algunas fuentes necesitan para identificar la referencia (Fasecolda): modelo (año), cilindraje, combustible y
/// pasajeros, leídos de <c>datos_consolidados.vehiculo</c>. Un dato técnico que el RUNT no trae va nulo (en gRPC, 0 o vacío).
/// Son datos del bien, sin documentos ni nombres.
/// </summary>
/// <example>
/// <code>
/// var solicitud = new SolicitudAvaluo("TST201", "9ZZPRUEBA00000001", 2020, 1598, "GASOLINA", 5);
/// </code>
/// </example>
public sealed record SolicitudAvaluo(string? Placa, string? Vin, int? Modelo, int? Cilindraje, string? Combustible, int? Pasajeros)
{
    /// <summary>Último carácter de la placa o, sin placa, del VIN (lo usa la simulación).</summary>
    public string Clave => Placa ?? Vin ?? string.Empty;
}

/// <summary>
/// Puerto del avalúo comercial (HU #13795, F05-C7): <c>ConsultasService.ConsultarAvaluos</c> de core-consultas, a nombre de la
/// empresa de la celda, que consulta en paralelo las fuentes habilitadas para la empresa (Fasecolda, MercadoLibre, base
/// gravable) y elige el valor sugerido con su fuente principal. Devuelve <see cref="ResultadoAvaluo"/> o el error tipado.
/// Ninguna fuente con valor NO es error: es <see cref="ResultadoAvaluo.SinValorComercial"/> (decisión Q4 = b). Implementaciones:
/// <see cref="FuenteAvaluoReal"/>, <see cref="FuenteAvaluoSimulada"/> (LOCAL o DEV) y <see cref="FuenteAvaluoNoConectada"/>,
/// elegidas por <see cref="SeleccionAdaptadores"/>. La usa <see cref="EjecutorCeldaAvaluo"/>.
/// </summary>
/// <example>
/// <code>
/// var r = await fuente.ConsultarAsync(celda.TenantId, solicitud, ct);
/// if (r.Error is { } error) return ResultadoCelda.Error(error.TipoError);
/// </code>
/// </example>
public interface IFuenteAvaluo
{
    Task<RespuestaFuente<ResultadoAvaluo>> ConsultarAsync(Guid empresa, SolicitudAvaluo solicitud, CancellationToken ct);
}
