using Flit.Tramites.Domain.RuntConfirmation;

namespace Flit.Tramites.Application.UseCases.RuntConfirmation;

/// <summary>
/// Arma la entrada del motor desde los crudos y decide. Lo único que tiene de aplicación es la
/// fecha de corte en día de Bogotá y la lectura del snapshot de radicación; la regla vive en dominio.
/// </summary>
public static class RuntConfirmationEvaluator
{
    /// <summary>Colombia no tiene horario de verano: UTC-5 fijo basta para pasar a día local.</summary>
    public static readonly TimeSpan BogotaOffset = TimeSpan.FromHours(-5);

    public static DateOnly CutoffDay(DateTimeOffset cutoffAt) =>
        DateOnly.FromDateTime(cutoffAt.ToOffset(BogotaOffset).DateTime);

    public static RuntConfirmationDecision Evaluate(
        RuntConfirmationCandidate candidate,
        string? primaryRawJson,
        string? sellerRawJson,
        string? baselineRawJson)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var input = new RuntConfirmationInput(
            candidate.ProcedureTypeCode,
            candidate.Family,
            CutoffDay(candidate.CutoffAt),
            Primary: primaryRawJson is null ? null : RuntVehicleSnapshotParser.Parse(primaryRawJson),
            Seller: sellerRawJson is null ? null : RuntVehicleSnapshotParser.Parse(sellerRawJson),
            Baseline: baselineRawJson is null ? null : RuntVehicleSnapshotParser.Parse(baselineRawJson),
            ExpectedPlate: candidate.Plate,
            TransitOfficeName: candidate.TransitOfficeName);

        return RuntConfirmationRules.Evaluate(input);
    }

    /// <summary>Qué deja un veredicto en el trámite (HU #12309 AC5/AC6). Error no toca nada.</summary>
    public static RuntConfirmationInstanceUpdate ApplyVerdict(
        RuntConfirmationDecision decision,
        int attemptsAfterThis,
        int discrepancyAfterRuns,
        int maxAttempts,
        DateTimeOffset now,
        out string? flag)
    {
        ArgumentNullException.ThrowIfNull(decision);

        switch (decision.Verdict)
        {
            case RuntConfirmationVerdict.Confirmed:
                flag = null;
                return new RuntConfirmationInstanceUpdate(IncrementAttempts: true, ConfirmedAt: now, Flag: null, ClearFlag: true);

            case RuntConfirmationVerdict.Discrepancy:
                flag = RuntConfirmationFlags.Discrepancia;
                return new RuntConfirmationInstanceUpdate(IncrementAttempts: true, ConfirmedAt: null, Flag: flag, ClearFlag: false);

            case RuntConfirmationVerdict.Unverifiable:
                flag = RuntConfirmationFlags.NoVerificable;
                return new RuntConfirmationInstanceUpdate(IncrementAttempts: true, ConfirmedAt: null, Flag: flag, ClearFlag: false);

            case RuntConfirmationVerdict.Pending:
                // Primero el tope (sale del universo), luego la discrepancia por reincidencia.
                flag = attemptsAfterThis >= maxAttempts
                    ? RuntConfirmationFlags.Tope
                    : attemptsAfterThis >= discrepancyAfterRuns
                        ? RuntConfirmationFlags.Discrepancia
                        : null;
                return new RuntConfirmationInstanceUpdate(IncrementAttempts: true, ConfirmedAt: null, Flag: flag, ClearFlag: false);

            default:
                flag = null;
                return new RuntConfirmationInstanceUpdate(IncrementAttempts: false, ConfirmedAt: null, Flag: null, ClearFlag: false);
        }
    }
}
