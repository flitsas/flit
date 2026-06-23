namespace Flit.Admin.Domain.OtRules;

/// <summary>Regla de negocio OT persistida en <c>admin.ot_feature_flags</c> (HU #10221).</summary>
public sealed class OtRule
{
    public Guid Id { get; init; }

    public Guid TenantId { get; init; }

    public bool IsEnabled { get; init; }

    public string Name { get; init; } = string.Empty;

    public IReadOnlyList<OtRuleCondition> Conditions { get; init; } = Array.Empty<OtRuleCondition>();

    public string Logic { get; init; } = OtRuleConstants.LogicAnd;

    public OtRuleAction Action { get; init; } = new();
}

public sealed class OtRuleCondition
{
    public string Field { get; init; } = string.Empty;

    public string Op { get; init; } = string.Empty;

    public string ValueJson { get; init; } = "null";
}

public sealed class OtRuleAction
{
    public string Type { get; init; } = string.Empty;

    public string? QueueName { get; init; }
}
