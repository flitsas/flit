namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>Perfil OT por tenant — <c>admin.transit_office_profiles</c> (HU #10152 / #10215).</summary>
public sealed class TransitOfficeProfile
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid TransitOfficeId { get; set; }

    public string OperationMode { get; set; } = "dashboard";

    public bool QuipuxReadOnly { get; set; }

    public long RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
