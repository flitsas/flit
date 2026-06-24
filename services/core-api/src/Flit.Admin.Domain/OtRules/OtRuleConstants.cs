namespace Flit.Admin.Domain.OtRules;

/// <summary>Constantes del motor de reglas OT (HU #10221, ADR-0018).</summary>
public static class OtRuleConstants
{
    public const string FlagKeyPrefix = "rule:";

    public const string LogicAnd = "AND";

    public const string LogicOr = "OR";

    public static readonly IReadOnlySet<string> SupportedLogic = new HashSet<string>(StringComparer.Ordinal)
    {
        LogicAnd,
        LogicOr,
    };

    public const string ActionBlock = "bloquear";

    public const string ActionBiometrics = "biometria";

    public const string ActionSpecialQueue = "cola_especial";

    public static readonly IReadOnlySet<string> SupportedActions = new HashSet<string>(StringComparer.Ordinal)
    {
        ActionBlock,
        ActionBiometrics,
        ActionSpecialQueue,
    };

    public const string OpEquals = "eq";

    public const string OpIn = "in";

    public static readonly IReadOnlySet<string> SupportedOperators = new HashSet<string>(StringComparer.Ordinal)
    {
        OpEquals,
        OpIn,
    };
}
