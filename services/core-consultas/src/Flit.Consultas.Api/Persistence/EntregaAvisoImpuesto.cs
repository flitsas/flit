namespace Flit.Consultas.Api.Persistence;

/// <summary>
/// Entrega de un aviso del proveedor de impuesto ya verificada (HU #14711): deduplica por (empresa, <c>X-Flit-Delivery</c>). Sin cuerpo,
/// firma ni datos personales; se purga a los 7 días.
/// </summary>
public sealed class EntregaAvisoImpuesto
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary><c>X-Flit-Delivery</c>, o <c>sin-delivery:{consulta}:{attempt}</c> si no vino.</summary>
    public string DeliveryId { get; set; } = "";

    /// <summary><c>consulta.id</c> del proveedor (el id externo del seguimiento).</summary>
    public string ExternalId { get; set; } = "";

    public int Attempt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
