using System.Text.Json;
using Flit.Admin.Domain.OtRules;

namespace Flit.Admin.Application.OtRules;

/// <summary>Motor de evaluación de reglas OT AND/OR (HU #10221 AC2–AC3, AC6).</summary>
public static class OtRuleEvaluator
{
    public static OtRuleEvaluationResponse Evaluate(OtRule rule, IReadOnlyDictionary<string, JsonElement> context)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(context);

        if (!rule.IsEnabled)
        {
            return new OtRuleEvaluationResponse { Matched = false };
        }

        var results = rule.Conditions.Select(c => EvaluateCondition(c, context)).ToList();
        var matched = rule.Logic.Equals(OtRuleConstants.LogicOr, StringComparison.Ordinal)
            ? results.Any(x => x)
            : results.All(x => x);

        if (!matched)
        {
            return new OtRuleEvaluationResponse { Matched = false };
        }

        return new OtRuleEvaluationResponse
        {
            Matched = true,
            ActionType = rule.Action.Type,
            QueueName = rule.Action.QueueName,
        };
    }

    public static IReadOnlyList<OtRuleEvaluationResponse> EvaluateAll(
        IReadOnlyList<OtRule> rules,
        IReadOnlyDictionary<string, JsonElement> context) =>
        rules.Select(r => Evaluate(r, context)).Where(r => r.Matched).ToList();

    private static bool EvaluateCondition(OtRuleCondition condition, IReadOnlyDictionary<string, JsonElement> context)
    {
        if (!context.TryGetValue(condition.Field, out var actual))
        {
            return false;
        }

        using var expectedDoc = JsonDocument.Parse(condition.ValueJson);
        var expected = expectedDoc.RootElement;

        return condition.Op switch
        {
            OtRuleConstants.OpEquals => JsonElementsEqual(actual, expected),
            OtRuleConstants.OpIn when expected.ValueKind == JsonValueKind.Array =>
                expected.EnumerateArray().Any(item => JsonElementsEqual(actual, item)),
            _ => false,
        };
    }

    private static bool JsonElementsEqual(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
        {
            if (left.ValueKind == JsonValueKind.String && right.ValueKind == JsonValueKind.String)
            {
                return string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal);
            }

            return false;
        }

        return left.ValueKind switch
        {
            JsonValueKind.True or JsonValueKind.False => left.GetBoolean() == right.GetBoolean(),
            JsonValueKind.String => string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal),
            JsonValueKind.Number => left.GetRawText() == right.GetRawText(),
            JsonValueKind.Array => left.GetRawText() == right.GetRawText(),
            JsonValueKind.Null => true,
            _ => left.GetRawText() == right.GetRawText(),
        };
    }
}
