using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Admin.Domain.OtRules;

namespace Flit.Admin.Application.OtRules;

public sealed class CreateOtRuleRequest
{
    public string Name { get; set; } = string.Empty;

    public IReadOnlyList<OtRuleConditionRequest> Conditions { get; set; } = Array.Empty<OtRuleConditionRequest>();

    public string Logic { get; set; } = OtRuleConstants.LogicAnd;

    public OtRuleActionRequest Action { get; set; } = new();
}

public sealed class OtRuleConditionRequest
{
    public string Field { get; set; } = string.Empty;

    public string Op { get; set; } = string.Empty;

    public JsonElement Value { get; set; }
}

public sealed class OtRuleActionRequest
{
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("queue_name")]
    public string? QueueName { get; set; }
}

public sealed class UpdateOtRuleRequest
{
    public bool? IsEnabled { get; set; }
}

public sealed class OtRuleResponse
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public bool IsEnabled { get; init; }

    public IReadOnlyList<OtRuleConditionResponse> Conditions { get; init; } = Array.Empty<OtRuleConditionResponse>();

    public string Logic { get; init; } = string.Empty;

    public OtRuleActionResponse Action { get; init; } = new();
}

public sealed class OtRuleConditionResponse
{
    public string Field { get; init; } = string.Empty;

    public string Op { get; init; } = string.Empty;

    public JsonElement Value { get; init; }
}

public sealed class OtRuleActionResponse
{
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("queue_name")]
    public string? QueueName { get; init; }
}

public sealed class OtRuleEvaluationResponse
{
    public bool Matched { get; init; }

    [JsonPropertyName("action")]
    public string? ActionType { get; init; }

    [JsonPropertyName("queue_name")]
    public string? QueueName { get; init; }
}

internal static class OtRuleMapper
{
    public static OtRuleResponse ToResponse(OtRule rule) => new()
    {
        Id = rule.Id,
        Name = rule.Name,
        IsEnabled = rule.IsEnabled,
        Logic = rule.Logic,
        Conditions = rule.Conditions.Select(c => new OtRuleConditionResponse
        {
            Field = c.Field,
            Op = c.Op,
            Value = JsonDocument.Parse(c.ValueJson).RootElement.Clone(),
        }).ToList(),
        Action = new OtRuleActionResponse
        {
            Type = rule.Action.Type,
            QueueName = rule.Action.QueueName,
        },
    };
}
