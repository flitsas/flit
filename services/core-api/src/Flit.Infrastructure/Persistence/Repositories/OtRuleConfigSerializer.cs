using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Admin.Domain.OtRules;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>Serializa/deserializa la estructura de reglas OT en <c>config jsonb</c> (HU #10221).</summary>
internal static class OtRuleConfigSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static string Serialize(
        string name,
        IReadOnlyList<OtRuleCondition> conditions,
        string logic,
        OtRuleAction action) =>
        JsonSerializer.Serialize(new OtRuleConfigDto
        {
            Name = name,
            Conditions = conditions.Select(c => new OtRuleConditionDto
            {
                Field = c.Field,
                Op = c.Op,
                Value = JsonDocument.Parse(c.ValueJson).RootElement.Clone(),
            }).ToList(),
            Logic = logic,
            Action = new OtRuleActionDto
            {
                Type = action.Type,
                QueueName = action.QueueName,
            },
        }, Options);

    public static OtRule Parse(Guid id, Guid tenantId, bool isEnabled, string configJson)
    {
        var dto = JsonSerializer.Deserialize<OtRuleConfigDto>(configJson, Options)
            ?? throw new InvalidOperationException("Config de regla OT inválido.");

        return new OtRule
        {
            Id = id,
            TenantId = tenantId,
            IsEnabled = isEnabled,
            Name = dto.Name,
            Logic = dto.Logic,
            Conditions = dto.Conditions.Select(c => new OtRuleCondition
            {
                Field = c.Field,
                Op = c.Op,
                ValueJson = c.Value.GetRawText(),
            }).ToList(),
            Action = new OtRuleAction
            {
                Type = dto.Action.Type,
                QueueName = dto.Action.QueueName,
            },
        };
    }

    private sealed class OtRuleConfigDto
    {
        public string Name { get; set; } = string.Empty;

        public List<OtRuleConditionDto> Conditions { get; set; } = [];

        public string Logic { get; set; } = OtRuleConstants.LogicAnd;

        public OtRuleActionDto Action { get; set; } = new();
    }

    private sealed class OtRuleConditionDto
    {
        public string Field { get; set; } = string.Empty;

        public string Op { get; set; } = string.Empty;

        public JsonElement Value { get; set; }
    }

    private sealed class OtRuleActionDto
    {
        public string Type { get; set; } = string.Empty;

        public string? QueueName { get; set; }
    }
}
