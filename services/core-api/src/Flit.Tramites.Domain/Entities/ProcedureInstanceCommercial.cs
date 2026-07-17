namespace Flit.Tramites.Domain.Entities;

/// <summary>
/// Datos comerciales del traspaso (1:1 con la instancia). Slice 1 — schema núcleo del rework de trámites.
/// </summary>
public sealed class ProcedureInstanceCommercial
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProcedureInstanceId { get; set; }
    public decimal? ValorVenta { get; set; }
    public string? Causal { get; set; }
    public decimal? TasaImpuesto { get; set; }
    public decimal? Derechos { get; set; }
    public string? MetodoPago { get; set; }

    // Feature #10707 — trazabilidad del avalúo comercial sugerido (AC#4).
    /// <summary>Valor sugerido (COP) que se mostró al gestor; null si no hubo sugerencia.</summary>
    public decimal? SuggestedValue { get; set; }
    /// <summary>Fuente del valor sugerido: <c>fasecolda</c> | <c>base_gravable</c> | <c>mercado_libre</c>.</summary>
    public string? SuggestedSource { get; set; }
    /// <summary>Origen del valor final capturado: <c>suggestion</c> | <c>manual</c>.</summary>
    public string? ValueOrigin { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public ProcedureInstance? ProcedureInstance { get; set; }
}
