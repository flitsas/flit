using Flit.Consultas.Grpc.V1;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>Una vigencia (año gravable) del impuesto en el payload de la celda: año, valor en pesos enteros y si está pagada.</summary>
public sealed record VigenciaResultadoImpuesto(int Anio, long Valor, bool Pagada);

/// <summary>
/// Resultado normalizado del impuesto departamental (HU #13862, F09-C5, decisión Q1 = a), guardado en el payload de la celda
/// IMPUESTO_DEPARTAMENTAL en snake_case: <c>monto_total</c> (suma de las vigencias no pagadas, pesos enteros; 0 sin deuda),
/// <c>vigencias</c> (<c>{anio, valor, pagada}</c>, ordenadas por año) y <c>departamento_codigo</c> consultado. Nunca el
/// proveedor, la transacción ni el origen (R-20): esos van solo al intento auditable.
/// </summary>
/// <example>
/// <code>
/// var normalizado = ResultadoImpuesto.Normalizar(respuesta, "05");
/// // { monto_total: 350000, vigencias: [{ anio: 2025, valor: 200000, pagada: false }, ...], departamento_codigo: "05" }
/// </code>
/// </example>
public sealed record ResultadoImpuesto(long MontoTotal, IReadOnlyList<VigenciaResultadoImpuesto> Vigencias, string DepartamentoCodigo)
{
    /// <summary>
    /// Lee el monto total y las vigencias de la respuesta gRPC; de <c>resultado</c> no copia nada (el proveedor solo va al
    /// intento). Montos negativos se leen como 0. Las vigencias quedan ordenadas por año.
    /// </summary>
    public static ResultadoImpuesto Normalizar(ConsultarImpuestoVehicularResponse respuesta, string departamentoCodigo)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        ArgumentException.ThrowIfNullOrWhiteSpace(departamentoCodigo);
        var vigencias = respuesta.Vigencias
            .OrderBy(v => v.Anio)
            .Select(v => new VigenciaResultadoImpuesto(v.Anio, Math.Max(v.Valor, 0), v.Pagada))
            .ToList();
        return new ResultadoImpuesto(Math.Max(respuesta.MontoTotal, 0), vigencias, departamentoCodigo.Trim());
    }
}
