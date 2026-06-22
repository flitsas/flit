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
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public ProcedureInstance? ProcedureInstance { get; set; }
}
