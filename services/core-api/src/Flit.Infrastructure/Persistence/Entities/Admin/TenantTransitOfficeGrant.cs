namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Grant que habilita un organismo de tránsito a un tenant —
/// <c>admin.tenant_transit_office_grants</c> (HU #10154 DDL, gestionado por HU #10192).
/// RLS por <c>app.current_tenant_id</c>. Unicidad por <c>(tenant_id, transit_office_id)</c>.
/// </summary>
public sealed class TenantTransitOfficeGrant
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid TransitOfficeId { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }
}
