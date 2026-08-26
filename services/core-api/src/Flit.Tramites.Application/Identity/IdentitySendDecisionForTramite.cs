using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.Identity;

/// <summary>
/// Resuelve candidatos (baúl / vigente / en vuelo) y evalúa la precedencia de envío para disparadores
/// de trámite (HU #11265). Capa PREVIA a la idempotencia por parte.
/// </summary>
public static class IdentitySendDecisionForTramite
{
    /// <summary>
    /// Evalúa si corresponde enviar correo de validación para el documento de la persona en el tenant.
    /// </summary>
    public static async Task<IdentitySendDecision> EvaluateAsync(
        IProcedureInstanceRepository repo,
        ISignatureVaultPolicy vaultPolicy,
        Guid tenantId,
        ProcedureInstanceActor? actor,
        string tipoDoc,
        string documento,
        IReadOnlyList<ProcedureInstanceBiometricValidation>? extraCandidates,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var candidates = new List<ProcedureInstanceBiometricValidation>();
        if (extraCandidates is { Count: > 0 })
            candidates.AddRange(extraCandidates);

        var vigente = await repo
            .FindVigenteApprovedByDocumentAsync(tenantId, tipoDoc, documento, now, ct)
            .ConfigureAwait(false);
        if (vigente is not null && candidates.TrueForAll(c => c.Id != vigente.Id))
            candidates.Add(vigente);

        var inFlight = await repo
            .ListInFlightByDocumentAsync(tenantId, tipoDoc, documento, ct)
            .ConfigureAwait(false);
        foreach (var v in inFlight)
        {
            if (candidates.TrueForAll(c => c.Id != v.Id))
                candidates.Add(v);
        }

        var hasBaul = false;
        if (FirmaBaulCobertura.Aplica(actor)
            && !string.IsNullOrWhiteSpace(tipoDoc)
            && !string.IsNullOrWhiteSpace(documento))
        {
            hasBaul = await vaultPolicy
                .ResolveAsync(tenantId, tipoDoc.Trim(), documento.Trim(), ct)
                .ConfigureAwait(false) is not null;
        }

        return IdentitySendDecisionEvaluator.Evaluate(new IdentitySendDecisionContext(
            tenantId,
            tipoDoc,
            documento,
            actor,
            hasBaul,
            candidates,
            now));
    }

    /// <summary>
    /// Interpreta la decisión para el handler de iniciar biométrica de instancia.
    /// <list type="bullet">
    /// <item><see cref="IdentitySendDecisionKind.NoEnviar"/> → bloquear con <c>biometria_activa</c>.</item>
    /// <item><see cref="IdentitySendDecisionKind.EncauzarReenvio"/> de la misma parte/instancia →
    /// dejar pasar al guard por parte (terminaliza enlace vencido y permite reenvío, AC4).</item>
    /// <item><see cref="IdentitySendDecisionKind.EncauzarReenvio"/> de otra validación →
    /// <c>enlace_vencido_reenvio</c> sin crear fila.</item>
    /// <item><see cref="IdentitySendDecisionKind.Enviar"/> → continuar.</item>
    /// </list>
    /// </summary>
    public static (string? Error, IdentitySendDecision? Conflict, bool Continue)
        ToStartOutcome(
            IdentitySendDecision decision,
            Guid instanceId,
            string? partyRole,
            IReadOnlyList<ProcedureInstanceBiometricValidation> instanceValidations)
    {
        if (decision.Kind == IdentitySendDecisionKind.Enviar)
            return (null, null, true);

        if (decision.Kind == IdentitySendDecisionKind.EncauzarReenvio
            && decision.ValidationId is Guid vid)
        {
            var sameParty = instanceValidations.FirstOrDefault(v =>
                v.Id == vid
                && v.ProcedureInstanceId == instanceId
                && string.Equals(v.PartyRole, partyRole, StringComparison.OrdinalIgnoreCase));
            if (sameParty is not null)
                return (null, null, true); // AC4: fall through al guard por parte
            return ("enlace_vencido_reenvio", decision, false);
        }

        if (decision.Kind == IdentitySendDecisionKind.NoEnviar)
            return ("biometria_activa", decision, false);

        return (null, null, true);
    }

    /// <summary>Cuerpo informativo cuando el índice único parcial rechaza un INSERT concurrente (HU #11266).</summary>
    public static IdentitySendDecision InFlightRaceConflict(string origen) =>
        new(
            IdentitySendDecisionKind.NoEnviar,
            IdentitySendMotivo.ValidacionEnVuelo,
            Origen: origen);
}
