namespace Flit.Consultas.Api.Impuesto;

/// <summary>Una vigencia leída. <paramref name="Pagada"/> queda en falso: el detalle del servicio es lo no pagado.</summary>
internal sealed record VigenciaLeida(int Anio, long Valor, bool Pagada, string? LiquidacionId, long? Avaluo);

/// <summary>
/// Lo que el intérprete puede afirmar. No lleva textos del proveedor.
/// <see cref="MontoTotal"/> es null cuando el servicio no informó un valor, también si escribió «0» habiendo deuda.
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
    string? UltimoPagoFecha);
