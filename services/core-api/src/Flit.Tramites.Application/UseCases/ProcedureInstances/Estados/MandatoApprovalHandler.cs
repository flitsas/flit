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
    /// El mandatario resuelto NO tiene identidad validada vigente (HU #10911/#10916): debe validar su
    /// identidad antes de firmar (⇒ 409 mandatario_identidad_requerida).
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
    IMandateSignerDirectory directory)
{
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
            .GetCandidatesAsync(transitOfficeId, instance.TenantId, ct)
            .ConfigureAwait(false);

        // HU #11203 — la elección hecha al REGISTRAR el trámite manda sobre la resolución automática:
        // el gestor ya dijo quién firma y el aprobador no tiene por qué volver a decidirlo. La elección
        // explícita del aprobador sigue teniendo la última palabra (es quien está aprobando), y la
        // resolución automática queda como respaldo para los trámites que no traen ninguna.
        var elegido = explicitSignerId ?? instance.MandateSignerId;

        var resolution = MandateSignerSelector.Resolve(candidates, approvingUserId, elegido);

        return resolution.Status switch
        {
            // Un único candidato / cotejo por usuario / selección explícita válida. El firmante debe tener
            // identidad validada VIGENTE (HU #10911/#10916): si no, se exige validar antes de aprobar.
            MandateSignerResolutionStatus.Resolved when resolution.Signer!.IdentityVigente =>
                new MandatoApprovalDecision(MandatoApprovalOutcome.Resolved, resolution.Signer.Id),
            MandateSignerResolutionStatus.Resolved =>
                new MandatoApprovalDecision(MandatoApprovalOutcome.IdentidadRequerida, null),
            // Varios sin match: el aprobador debe elegir (409).
            MandateSignerResolutionStatus.RequiereSeleccion =>
                new MandatoApprovalDecision(MandatoApprovalOutcome.RequiereSeleccion, null),
            // Sin mandatarios configurados (p. ej. Sabaneta institucional): aprobar sin firmante persona.
            _ => new MandatoApprovalDecision(MandatoApprovalOutcome.NotApplicable, null),
        };
    }
}
