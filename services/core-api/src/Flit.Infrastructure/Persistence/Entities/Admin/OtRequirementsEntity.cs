namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>Requisitos por OT — <c>admin.ot_requirements</c> (HU #10545).</summary>
public sealed class OtRequirementsEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid TransitOfficeId { get; set; }

    public bool RequiresRnmc { get; set; }

    public bool AllowPlatePreassign { get; set; }

    public bool IdentityValidationEnabled { get; set; } = true;

    public long RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
