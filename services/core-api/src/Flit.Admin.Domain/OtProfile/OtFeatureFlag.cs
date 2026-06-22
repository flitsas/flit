namespace Flit.Admin.Domain.OtProfile;

/// <summary>Feature flag por tenant OT (admin.ot_feature_flags).</summary>
public sealed class OtFeatureFlag
{
    public Guid Id { get; init; }

    public Guid TenantId { get; init; }

    public string FlagKey { get; init; } = string.Empty;

    public bool IsEnabled { get; init; }

    public string ConfigJson { get; init; } = "{}";
}
