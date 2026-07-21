using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// Gate puro (sin IO) del paso de solicitud de placa (FEATURE-08 / CFD-08). El paso
/// <c>plate_request</c> solo aplica cuando <c>gate_profile.requiresPlateRequest = true</c>; si aplica,
/// bloquea con <c>PLATE_REQUEST_PENDING</c> hasta que la solicitud de placa esté completada. Lo
/// consumirá el <c>DynamicGateEvaluator</c>/<c>WizardStateQuery</c> del wizard dinámico (HU-BE-06),
/// que decide con <see cref="AppliesTo"/> si incluir el paso en el árbol.
/// </summary>
public static class PlateRequestGate
{
    /// <summary>Código de bloqueo mientras la solicitud de placa no esté completada (→ canSubmit=false).</summary>
    public const string PlateRequestPending = "PLATE_REQUEST_PENDING";

    /// <summary><c>section_type</c> del paso de placa en el registry del wizard.</summary>
    public const string SectionType = "plate_request";

    /// <summary>El paso de placa aplica al tipo (se incluye en el árbol del wizard) solo si el flag está activo.</summary>
    public static bool AppliesTo(ProcedureTypeGateProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.RequiresPlateRequest;
    }

    /// <summary>
    /// Evalúa el gate del paso de placa. Si el flag no aplica → <see cref="GateResult.Allowed"/> (paso
    /// omitido). Si aplica y la solicitud no está completada → bloqueo <c>PLATE_REQUEST_PENDING</c>.
    /// </summary>
    public static GateResult Evaluate(ProcedureTypeGateProfile profile, bool plateRequestCompleted)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (!profile.RequiresPlateRequest)
            return GateResult.Allowed;

        return plateRequestCompleted
            ? GateResult.Allowed
            : GateResult.Block(PlateRequestPending, "La solicitud de placa está pendiente.");
    }
}
