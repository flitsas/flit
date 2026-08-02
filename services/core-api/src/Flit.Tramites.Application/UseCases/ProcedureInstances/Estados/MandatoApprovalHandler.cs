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
    ISignatureVaultPolicy? vaultPolicy = null)
{
    // El mandatario firma igual que cualquier otra parte: con la firma del baúl si la tiene, y si no con
    // el sello de su validación de identidad (misma precedencia que aplica el generador del mandato).
    // Default inerte ⇒ sin baúl configurado el gate se comporta como antes.
    private readonly ISignatureVaultPolicy _vaultPolicy = vaultPolicy ?? NullSignatureVaultPolicy.Instance;

    public async Task<MandatoApprovalDecision> CheckAsync(
        Guid instanceId,
        Guid clientTenantId,
        Guid? approvingUserId,
        Guid? explicitSignerId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithFurGraphAsync(instanceId, clientTenantId, ct).ConfigureAwait(false);
        if (instance is null)
            return new MandatoApprovalDecision(MandatoApprovalOutcome.NotApplicable, null);

        // El mandato aplica sii ya se generó su adjunto (en preparado, cuando ExigeMandato). Sin él, no se
        // exige firmante — evita el 409 espurio en trámites que no requieren mandato pero cuyo OT sí tiene
        // mandatarios registrados.
        var exigeMandato = instance.Attachments.Any(a =>
            string.Equals(a.Tipo, "mandato", StringComparison.OrdinalIgnoreCase));
        if (!exigeMandato || instance.TransitOfficeId is not { } transitOfficeId)
            return new MandatoApprovalDecision(MandatoApprovalOutcome.NotApplicable, null);

        var candidates = await directory
            .GetCandidatesAsync(
                transitOfficeId, instance.TenantId,
                MandateSignerSelectionResolver.ResolveNitMandante(instance), ct)
            .ConfigureAwait(false);

        // HU #11203 — la elección hecha al REGISTRAR el trámite manda sobre la resolución automática:
        // el gestor ya dijo quién firma y el aprobador no tiene por qué volver a decidirlo. La elección
        // explícita del aprobador sigue teniendo la última palabra (es quien está aprobando), y la
        // resolución automática queda como respaldo para los trámites que no traen ninguna.
        var elegido = explicitSignerId ?? instance.MandateSignerId;

        var resolution = MandateSignerSelector.Resolve(candidates, approvingUserId, elegido);

        // El gate miraba SOLO la identidad, así que bloqueaba con "mandatario_identidad_requerida" a un
        // mandatario que tenía su firma del baúl vigente y podía firmar perfectamente. Son alternativas,
        // no requisitos acumulativos: basta cualquiera de las tres.
        //
        // Y quien firma A MANO ante ese organismo no necesita ninguna: el documento le deja la línea y
        // él la suscribe en papel. Exigirle firma del baúl o identidad bloquearía un mandato que se
        // firma justamente porque no las tiene.
        var puedeFirmar = resolution.Status == MandateSignerResolutionStatus.Resolved
            && (resolution.Signer!.FirmaFisica
                || resolution.Signer.IdentityVigente
                || await TieneFirmaDelBaulAsync(instance.TenantId, resolution.Signer, ct).ConfigureAwait(false));

        return resolution.Status switch
        {
            MandateSignerResolutionStatus.Resolved when puedeFirmar =>
                new MandatoApprovalDecision(MandatoApprovalOutcome.Resolved, resolution.Signer!.Id),
            MandateSignerResolutionStatus.Resolved =>
                new MandatoApprovalDecision(MandatoApprovalOutcome.IdentidadRequerida, null),
            // Varios sin match: el aprobador debe elegir (409).
            MandateSignerResolutionStatus.RequiereSeleccion =>
                new MandatoApprovalDecision(MandatoApprovalOutcome.RequiereSeleccion, null),
            // Sin mandatarios configurados (p. ej. Sabaneta institucional): aprobar sin firmante persona.
            _ => new MandatoApprovalDecision(MandatoApprovalOutcome.NotApplicable, null),
        };
    }

    /// <summary>
    /// ¿El mandatario tiene firma del baúl activa y vigente? Se resuelve por su DOCUMENTO y contra el
    /// tenant de la compañía gestora, igual que hace el generador del mandato (HU #11030):
    /// <c>mandate_signers.signature_vault_id</c> no se escribe nunca, así que esa FK no sirve para saberlo.
    /// </summary>
    private async Task<bool> TieneFirmaDelBaulAsync(
        Guid clientTenantId, MandateSignerCandidate signer, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(signer.Documento))
            return false;

        var tipoDoc = string.IsNullOrWhiteSpace(signer.TipoDocumento) ? "CC" : signer.TipoDocumento.Trim();
        return await _vaultPolicy
            .ResolveAsync(clientTenantId, tipoDoc, signer.Documento.Trim(), ct)
            .ConfigureAwait(false) is not null;
    }
}
