namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Bloqueo de organismo de tránsito para una cabeza Marca Blanca —
/// <c>admin.tenant_transit_office_blocks</c> (HU #12407).
/// </summary>
public sealed class TenantTransitOfficeBlock
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid TransitOfficeId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }
}
