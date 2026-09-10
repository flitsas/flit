using Flit.Tramites.Domain.RuntConfirmation;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.RuntConfirmation;

public sealed record ConsultNowCommand(Guid ProcedureInstanceId, Guid? ActorUserId);

public enum ConsultNowStatus
{
    Ok,
    NotFound,
    /// <summary>409: no está en Aprobado, el tipo está fuera de alcance o ya está confirmado.</summary>
    Conflict,
}

public sealed record ConsultNowResult(ConsultNowStatus Status, string? ConflictCode, RuntConfirmationAttempt? Attempt, RuntConfirmationRun? Run);

/// <summary>
/// «Consultar ahora» (HU #12310 AC5/AC6): una corrida manual de UN trámite con el proveedor
/// configurado. Se permite con el interruptor apagado y con el trámite en tope o no verificable —es
/// un acto explícito de alguien con permiso—, pero no sobre lo que no tiene sentido consultar: no
/// aprobado, tipo fuera de alcance o ya confirmado. Cuenta como intento igual que uno programado.
/// </summary>
public sealed class ConsultNowHandler(IRuntConfirmationStore store, RuntConfirmationRunner runner)
{
    public const string NoAprobado = "tramite_no_aprobado";
    public const string FueraDeAlcance = "tipo_fuera_de_alcance";
    public const string YaConfirmado = "ya_confirmado";

    public async Task<ConsultNowResult> HandleAsync(ConsultNowCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var candidate = await store.GetCandidateAsync(command.ProcedureInstanceId, ct).ConfigureAwait(false);
        if (candidate is null)
            return new(ConsultNowStatus.NotFound, null, null, null);

        var status = await store.GetProcedureStatusAsync(command.ProcedureInstanceId, ct).ConfigureAwait(false);
        if (!string.Equals(status, TramiteEstado.Aprobado, StringComparison.Ordinal))
            return new(ConsultNowStatus.Conflict, NoAprobado, null, null);

        if (RuntProcedureEquivalence.ExcludedProcedureTypeCodes.Contains(candidate.ProcedureTypeCode, StringComparer.OrdinalIgnoreCase))
            return new(ConsultNowStatus.Conflict, FueraDeAlcance, null, null);

        if (candidate.RuntConfirmedAt is not null)
            return new(ConsultNowStatus.Conflict, YaConfirmado, null, null);

        var run = await runner.RunAsync(
            new RuntConfirmationRunRequest(RuntConfirmationRunTriggers.Manual, [candidate.InstanceId], command.ActorUserId), ct)
            .ConfigureAwait(false);

        var attempt = await store.GetLatestAttemptForRunAsync(run.Id, candidate.InstanceId, ct).ConfigureAwait(false);
        return new(ConsultNowStatus.Ok, null, attempt, run);
    }
}
