using Flit.Consultas.Grpc.V1;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>Una vigencia (año gravable) del impuesto en el payload de la celda: año, valor en pesos enteros y si está pagada.</summary>
public sealed record VigenciaResultadoImpuesto(int Anio, long Valor, bool Pagada, string? LiquidacionId = null, long? Avaluo = null);

/// <summary>Último pago informado. Un campo vacío queda null: no se inventa cero ni fecha.</summary>
public sealed record UltimoPagoResultadoImpuesto(int? Anio, long? Valor, string? Fecha);

/// <summary>
/// Resultado normalizado del impuesto departamental, guardado en el payload de la celda IMPUESTO_DEPARTAMENTAL en snake_case.
/// <c>tiene_deuda</c> es la confirmación. <c>monto_total</c> es null cuando el servicio no informó valor (también con deuda).
/// El vacío de un opcional no se convierte en cero. Nunca el proveedor, la transacción ni el origen (R-20).
/// </summary>
public sealed record ResultadoImpuesto(
    long? MontoTotal,
    IReadOnlyList<VigenciaResultadoImpuesto> Vigencias,
    string DepartamentoCodigo,
    bool TieneDeuda = false,
    long? InteresesMora = null,
    long? Sanciones = null,
    long? AvaluoComercial = null,
    UltimoPagoResultadoImpuesto? UltimoPago = null,
    string? LiquidacionConsolidadaId = null,
    string? PeriodoCertificacion = null,
    string? FechaConsultaProveedor = null)
{
    /// <summary>
    /// Lee el monto, las vigencias y los opcionales. <c>monto_total</c> solo si <c>monto_informado</c>; un negativo es sin dato.
    /// Las cadenas en blanco quedan null.
    /// </summary>
    public static ResultadoImpuesto Normalizar(ConsultarImpuestoVehicularResponse respuesta, string departamentoCodigo)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        ArgumentException.ThrowIfNullOrWhiteSpace(departamentoCodigo);
        var vigencias = respuesta.Vigencias
            .OrderBy(v => v.Anio)
            .Select(v => new VigenciaResultadoImpuesto(
                v.Anio,
                Math.Max(v.Valor, 0),
                v.Pagada,
                v.HasLiquidacionId ? Texto(v.LiquidacionId) : null,
                v.HasAvaluo ? NoNegativo(v.Avaluo) : null))
            .ToList();
        return new ResultadoImpuesto(
            respuesta.MontoInformado ? NoNegativo(respuesta.MontoTotal) : null,
            vigencias,
            departamentoCodigo.Trim(),
            respuesta.TieneDeuda,
            respuesta.HasInteresesMora ? NoNegativo(respuesta.InteresesMora) : null,
            respuesta.HasSanciones ? NoNegativo(respuesta.Sanciones) : null,
            respuesta.HasAvaluoComercial ? NoNegativo(respuesta.AvaluoComercial) : null,
            Pago(respuesta.UltimoPago),
            respuesta.HasLiquidacionConsolidadaId ? Texto(respuesta.LiquidacionConsolidadaId) : null,
            respuesta.HasPeriodoCertificacion ? Texto(respuesta.PeriodoCertificacion) : null,
            respuesta.HasFechaConsultaProveedor ? Texto(respuesta.FechaConsultaProveedor) : null);
    }

    private static long? NoNegativo(long valor) => valor < 0 ? null : valor;

    private static string? Texto(string? valor)
    {
        var limpio = valor?.Trim();
        return string.IsNullOrEmpty(limpio) ? null : limpio;
    }

    private static UltimoPagoResultadoImpuesto? Pago(UltimoPagoImpuesto? pago)
    {
        if (pago is null)
            return null;
        var anio = pago.HasAnio ? pago.Anio : (int?)null;
        var valor = pago.HasValor ? NoNegativo(pago.Valor) : null;
        var fecha = pago.HasFecha ? Texto(pago.Fecha) : null;
        return anio is null && valor is null && fecha is null ? null : new UltimoPagoResultadoImpuesto(anio, valor, fecha);
    }
}
