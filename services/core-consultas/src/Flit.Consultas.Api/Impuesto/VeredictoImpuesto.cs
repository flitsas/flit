namespace Flit.Consultas.Api.Impuesto;

internal enum TipoAdvertenciaLeida { Validacion, Aviso }

internal sealed record AdvertenciaLeida(TipoAdvertenciaLeida Tipo, string Campo, string Codigo, int? Anio);

/// <summary>Una vigencia leída. <paramref name="Pagada"/> queda en falso: el detalle de la fuente es lo no pagado.</summary>
internal sealed record VigenciaLeida(
    int Anio,
    long Valor,
    bool Pagada,
    string? LiquidacionId,
    long? Avaluo,
    string? ReferenciaPago,
    string? FechaVencimiento);

/// <summary>
/// Lo que el intérprete puede afirmar. No lleva textos del proveedor.
/// <see cref="MontoTotal"/> es null cuando la fuente no informó un valor, también si escribió «0» habiendo deuda.
/// <para>
/// <see cref="EstadoFuente"/> (<c>status</c>), <see cref="EstadoResultadoFuente"/> (<c>result.status</c>) y
/// <see cref="CodigoErrorFuente"/> (<c>error_code</c>) son solo para el log de diagnóstico de core-consultas: pasan la forma
/// <c>^[a-z0-9_]{1,64}$</c> o quedan en <c>no_reconocido</c>. No viajan al proto ni a Diagnóstico.
/// </para>
/// </summary>
internal sealed record VeredictoImpuesto(
    bool Confirmado,
    string? Codigo,
    bool TieneDeuda,
    long? MontoTotal,
    bool MontoInformado,
    bool DatoParcial,
    IReadOnlyList<VigenciaLeida> Vigencias,
    long? InteresesMora,
    long? Sanciones,
    long? AvaluoComercial,
    string? LiquidacionConsolidadaId,
    string? PeriodoCertificacion,
    string? FechaConsulta,
    int? UltimoPagoAnio,
    long? UltimoPagoValor,
    string? UltimoPagoFecha,
    IReadOnlyList<AdvertenciaLeida> Advertencias,
    string? EstadoFuente = null,
    string? EstadoResultadoFuente = null,
    string? CodigoErrorFuente = null)
{
    public override string ToString() =>
        $"VeredictoImpuesto {{ Confirmado = {Confirmado}, Codigo = {Codigo}, TieneDeuda = {TieneDeuda}, MontoTotal = {MontoTotal}, DatoParcial = {DatoParcial}, Vigencias = {Vigencias.Count}, Advertencias = {Advertencias.Count} }}";
}
