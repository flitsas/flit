namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>Feature flag OT por tenant — <c>admin.ot_feature_flags</c> (HU #10152 / #10215).</summary>
public sealed class OtFeatureFlagEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string FlagKey { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    public string Config { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
