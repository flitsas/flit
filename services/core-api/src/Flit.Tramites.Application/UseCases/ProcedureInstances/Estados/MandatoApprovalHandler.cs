using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;

/// <summary>Desenlace de la resolución del mandatario al aprobar (ADR-0036 §D9, HU #10916).</summary>
public enum MandatoApprovalOutcome
{
    /// <summary>El trámite no exige mandato-persona (no aplica, institucional, o sin mandatarios): aprobar sin firmante.</summary>
    NotApplicable,

    /// <summary>Firmante determinado (único, cotejo por usuario, o selección explícita válida): aprobar con él.</summary>
    Resolved,

    /// <summary>Varios mandatarios sin cotejo único: el aprobador debe elegir uno (⇒ 409 mandatario_requerido).</summary>
    RequiereSeleccion,

    /// <summary>
    /// El mandatario resuelto no tiene NINGUNA de las dos formas de firmar —ni firma del baúl vigente ni
    /// identidad validada vigente—, así que debe conseguir una antes de firmar
    /// (⇒ 409 mandatario_identidad_requerida).
    /// </summary>
    IdentidadRequerida,
}

/// <summary>Decisión de la resolución del mandatario al aprobar; <c>MandateSignerId</c> solo con <see cref="MandatoApprovalOutcome.Resolved"/>.</summary>
public sealed record MandatoApprovalDecision(MandatoApprovalOutcome Outcome, Guid? MandateSignerId);

/// <summary>
/// Resuelve QUÉ mandatario firma el mandato al aprobar un trámite (ADR-0036 §D9, HU #10916). Es la
/// pieza que la ruta de aprobación del OT (que NO pasa por <c>TramiteLifecycleService</c>) consume desde
/// el endpoint: el módulo Admin no puede referenciar Trámites, así que la orquestación vive en el API y
/// esta comprobación (read-only) en Trámites. El mandato APLICA sii ya existe su adjunto <c>mandato</c>
/// (generado en preparado cuando <c>ExigeMandato</c>): así no se exige firmante a trámites que no lo
/// requieren aunque el OT tenga mandatarios. Los candidatos salen del <see cref="IMandateSignerDirectory"/>
/// (tablas admin, sin RLS) y la regla es la pura <see cref="MandateSignerSelector"/>.
/// </summary>
public sealed class MandatoApprovalHandler(
    IProcedureInstanceRepository repo,
    IMandateSignerDirectory directory,
    ISignatureVaultPolicy? vaultPolicy = null,
    IMandateRequirementPolicy? mandatePolicy = null)
{
    private readonly IMandateRequirementPolicy _mandatePolicy = mandatePolicy ?? NullMandateRequirementPolicy.Instance;

    public async Task<MandatoApprovalDecision> CheckAsync(
        Guid instanceId,
        Guid clientTenantId,
        Guid? approvingUserId,
        Guid? explicitSignerId,
        CancellationToken ct = default)
    {
        _ = vaultPolicy;
        var instance = await repo.GetByIdWithFurGraphAsync(instanceId, clientTenantId, ct).ConfigureAwait(false);
        if (instance is null)
            return new MandatoApprovalDecision(MandatoApprovalOutcome.NotApplicable, null);

        // El mandato aplica sii ya se generó su adjunto DEL SISTEMA (en preparado, cuando ExigeMandato).
        // Sin él, no se exige firmante — evita el 409 espurio en trámites que no requieren mandato pero
        // cuyo OT sí tiene mandatarios registrados.
        //
        // HU #11317 (Feature #11309, ADR-0042 §supersede parcial) — excluye los adjuntos de mandato con
        // Source="company": ese PDF es un documento ESTÁTICO de la compañía (sin bloques de firma del
        // mandatario, sin consultar directorio ni política de firma), así que su sola presencia NO
        // implica que el trámite exija un mandatario que firme. Si el gate lo mirara sin distinguir el
        // origen, un mandato personalizado bloquearía SIEMPRE la aprobación con 409
        // mandatario_requerido/mandatario_identidad_requerida, aunque nadie vaya a firmarlo. Cuando el
        // adjunto es del sistema (Source="system", el caso de siempre) el gate exige mandatario
        // exactamente como antes.
        var exigeMandato = instance.Attachments.Any(a =>
            string.Equals(a.Tipo, "mandato", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(a.Source, "company", StringComparison.OrdinalIgnoreCase));
        if (!exigeMandato || instance.TransitOfficeId is not { } transitOfficeId)
            return new MandatoApprovalDecision(MandatoApprovalOutcome.NotApplicable, null);

        var officeCode = instance.FieldValues.FirstOrDefault(f =>
            string.Equals(f.FieldKey, "transit_office_code", StringComparison.OrdinalIgnoreCase))?.ValueText;
        var mandateConfig = string.IsNullOrWhiteSpace(officeCode)
            ? null
            : await _mandatePolicy.ResolveAsync(officeCode, clientTenantId, ct).ConfigureAwait(false);

        // Abierto / institucional: aprobar sin firmante persona (tipo por compañía×OT).
        if (MandatoAssignmentModeCodes.SkipsPersonSigner(mandateConfig?.AssignmentMode))
        {
            return new MandatoApprovalDecision(MandatoApprovalOutcome.NotApplicable, null);
        }

        var candidates = await directory
            .GetCandidatesAsync(
                transitOfficeId, instance.TenantId,
                MandateSignerSelectionResolver.ResolveNitMandante(instance), ct)
            .ConfigureAwait(false);
        candidates = await MandateSignerSelectionResolver
            .WithOtDefaultAsync(candidates, mandateConfig?.OtDefaultMandateSignerId, directory, ct)
            .ConfigureAwait(false);

        var elegido = MandateSignerDefaultResolver.Resolve(
            candidates.Select(c => c.Id).ToList(),
            explicitSignerId ?? instance.MandateSignerId,
            mandateConfig?.OtDefaultMandateSignerId,
            mandateConfig?.DefaultMandateSignerId);

        var resolution = MandateSignerSelector.Resolve(candidates, approvingUserId, elegido);

        // El OT puede emitir el mandato en blanco (sin identidad, baúl ni firma a mano).
        // Quién firma sigue resolviéndose; cómo firma no bloquea la aceptación.
        return resolution.Status switch
        {
            MandateSignerResolutionStatus.Resolved =>
                new MandatoApprovalDecision(MandatoApprovalOutcome.Resolved, resolution.Signer!.Id),
            MandateSignerResolutionStatus.RequiereSeleccion =>
                new MandatoApprovalDecision(MandatoApprovalOutcome.RequiereSeleccion, null),
            _ => new MandatoApprovalDecision(MandatoApprovalOutcome.NotApplicable, null),
        };
    }
}
