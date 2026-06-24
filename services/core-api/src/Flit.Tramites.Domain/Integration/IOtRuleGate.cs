namespace Flit.Tramites.Domain.Integration;

/// <summary>Resultado de evaluación del motor de reglas OT en runtime (HU #10221 RF06–08).</summary>
public sealed record OtRuleGateResult(
    bool IsBlocked,
    string? ActionType,
    string? QueueName,
    string? ErrorCode)
{
    public static OtRuleGateResult Allowed() => new(false, null, null, null);

    public static OtRuleGateResult Blocked(string? actionType = "bloquear") =>
        new(true, actionType, null, "ot_rule_blocked");

    public static OtRuleGateResult BiometricRequired() =>
        new(true, "biometria", null, "biometria_requerida_ot");

    public static OtRuleGateResult SpecialQueue(string queueName) =>
        new(false, "cola_especial", queueName, null);
}

/// <summary>
/// Puerto para evaluar reglas OT activas sin acoplar trámites al módulo Admin.
/// </summary>
public interface IOtRuleGate
{
    Task<OtRuleGateResult> EvaluateSubmissionAsync(
        Guid? transitOfficeId,
        Guid procedureTypeId,
        string? procedureTypeCode,
        CancellationToken cancellationToken = default);
}

/// <summary>Implementación nula cuando no hay reglas OT configuradas.</summary>
public sealed class NullOtRuleGate : IOtRuleGate
{
    public static NullOtRuleGate Instance { get; } = new();

    public Task<OtRuleGateResult> EvaluateSubmissionAsync(
        Guid? transitOfficeId,
        Guid procedureTypeId,
        string? procedureTypeCode,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OtRuleGateResult.Allowed());
}
