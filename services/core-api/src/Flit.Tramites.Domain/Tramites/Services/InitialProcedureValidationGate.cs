using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// Señales de contexto para las validaciones iniciales configurables (CFD-03). Cada campo es
/// <c>bool?</c>: <c>null</c> = no evaluada/desconocida (no bloquea). Permite que el gate sea puro:
/// el handler resuelve las señales disponibles al crear la instancia y deja en <c>null</c> las que
/// no aplican todavía (p. ej. la duplicidad por placa/VIN, que no existe hasta el paso 1 del wizard).
/// </summary>
public sealed record InitialProcedureValidationContext(
    bool? CompanyRuleSatisfied,
    bool? OtAuthorized,
    bool? DuplicateActiveExists);

/// <summary>
/// Gate puro (sin IO) de las validaciones iniciales activables por <c>gate_profile</c> (CFD-03):
/// regla de compañía, operabilidad del OT y duplicidad de trámite. Devuelve el primer bloqueo
/// aplicable con su código canónico, o <see cref="GateResult.Allowed"/> si nada bloquea. Con todos
/// los flags en <c>false</c> nunca bloquea (BE-02-AC-06).
/// </summary>
public static class InitialProcedureValidationGate
{
    /// <summary>La compañía/OT no cumple la regla de compañía del tipo (→ 422).</summary>
    public const string CompanyRuleViolation = "COMPANY_RULE_VIOLATION";

    /// <summary>El OT no está habilitado/operable para este tipo (→ 422).</summary>
    public const string OtNotAuthorized = "OT_NOT_AUTHORIZED_FOR_TYPE";

    /// <summary>Ya existe un trámite activo del mismo tipo para la placa/VIN (→ 409).</summary>
    public const string DuplicateActiveProcedure = "DUPLICATE_ACTIVE_PROCEDURE";

    public static GateResult Evaluate(
        ProcedureTypeGateProfile profile,
        InitialProcedureValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(context);

        if (profile.ValidateCompanyRule && context.CompanyRuleSatisfied == false)
            return GateResult.Block(
                CompanyRuleViolation, "El OT del operador no cumple la regla de compañía del tipo.");

        if (profile.ValidateOtOperability && context.OtAuthorized == false)
            return GateResult.Block(
                OtNotAuthorized, "El OT del operador no está habilitado/operable para este tipo.");

        if (profile.ValidateDuplicateProcedure && context.DuplicateActiveExists == true)
            return GateResult.Block(
                DuplicateActiveProcedure, "Ya existe un trámite activo del mismo tipo para la placa/VIN.");

        return GateResult.Allowed;
    }
}
