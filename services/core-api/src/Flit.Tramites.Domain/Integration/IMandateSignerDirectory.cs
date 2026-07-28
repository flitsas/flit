namespace Flit.Tramites.Domain.Integration;

/// <summary>
/// Un mandatario candidato para firmar el mandato de un trámite (ADR-0036, HU #10916): el firmante
/// (<c>admin.mandate_signers</c>) activo asignado a la compañía gestora en el OT del trámite. <c>UserId</c>
/// es la cuenta de OT vinculada (§D9): la llave del cotejo automático con el usuario que aprueba.
/// <c>IdentityVigente</c> (HU #10916/#10911): el mandatario tiene una validación de identidad admin
/// APROBADA y VIGENTE (se valida una vez y se apalanca en los mandatos mientras esté vigente); un
/// mandatario sin identidad vigente NO puede firmar (se exige validar antes de aprobar).
/// </summary>
public sealed record MandateSignerCandidate(
    Guid Id, string Nombre, string Documento, Guid? UserId, bool IdentityVigente = true);

/// <summary>
/// Puerto para consultar los mandatarios registrados por el OT para una compañía gestora (ADR-0036,
/// HU #10916). Desacopla el módulo de trámites del de Admin (mismo patrón que
/// <see cref="IMandateRequirementPolicy"/>). La resolución del firmante (auto/selección) NO vive aquí:
/// es la función pura <see cref="MandateSignerSelector.Resolve"/>. Este puerto solo lee.
/// </summary>
public interface IMandateSignerDirectory
{
    /// <summary>
    /// Mandatarios ACTIVOS asignados a la compañía <paramref name="companyTenantId"/> en el OT
    /// <paramref name="transitOfficeId"/> (join <c>mandate_signers ↔ mandate_signer_companies</c>).
    /// Lista vacía si el OT/compañía no tiene mandatarios configurados.
    /// </summary>
    Task<IReadOnlyList<MandateSignerCandidate>> GetCandidatesAsync(
        Guid transitOfficeId, Guid companyTenantId, CancellationToken cancellationToken = default);

    /// <summary>El mandatario por su id (para rellenar el PDF al regenerar), o <c>null</c> si no existe/inactivo.</summary>
    Task<MandateSignerCandidate?> GetByIdAsync(Guid mandateSignerId, CancellationToken cancellationToken = default);
}

/// <summary>Directorio seguro que NUNCA resuelve mandatarios (para dominio/tests que no lo ejercitan).</summary>
public sealed class NullMandateSignerDirectory : IMandateSignerDirectory
{
    public static NullMandateSignerDirectory Instance { get; } = new();

    public Task<IReadOnlyList<MandateSignerCandidate>> GetCandidatesAsync(
        Guid transitOfficeId, Guid companyTenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MandateSignerCandidate>>([]);

    public Task<MandateSignerCandidate?> GetByIdAsync(
        Guid mandateSignerId, CancellationToken cancellationToken = default) =>
        Task.FromResult<MandateSignerCandidate?>(null);
}

/// <summary>Estado de la resolución del firmante del mandato al aprobar (ADR-0036, HU #10916).</summary>
public enum MandateSignerResolutionStatus
{
    /// <summary>Firmante único determinado (uno solo, cotejo por usuario, o selección explícita válida).</summary>
    Resolved,

    /// <summary>Varios candidatos sin cotejo único: el aprobador debe elegir uno explícitamente (409).</summary>
    RequiereSeleccion,

    /// <summary>El OT/compañía no tiene mandatarios configurados: no hay firmante persona que asignar.</summary>
    NoConfigurado,
}

/// <summary>Resultado de la resolución del firmante: estado + candidato elegido (null salvo <c>Resolved</c>).</summary>
public sealed record MandateSignerResolution(MandateSignerResolutionStatus Status, MandateSignerCandidate? Signer);

/// <summary>
/// Regla PURA de resolución del firmante del mandato al aprobar (ADR-0036 §D9, HU #10916):
/// <list type="bullet">
///   <item>Selección explícita: válida solo si el id está entre los candidatos; si no, exige elegir.</item>
///   <item>Cero candidatos ⇒ <see cref="MandateSignerResolutionStatus.NoConfigurado"/>.</item>
///   <item>Un candidato ⇒ auto.</item>
///   <item>Varios ⇒ cotejo por la cuenta de usuario que aprueba; match ÚNICO ⇒ auto; si no, exige elegir.</item>
/// </list>
/// </summary>
public static class MandateSignerSelector
{
    public static MandateSignerResolution Resolve(
        IReadOnlyList<MandateSignerCandidate> candidates,
        Guid? approvingUserId,
        Guid? explicitSignerId)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        // Selección explícita del aprobador: debe pertenecer al conjunto de candidatos válidos.
        if (explicitSignerId is { } chosenId && chosenId != Guid.Empty)
        {
            var chosen = candidates.FirstOrDefault(c => c.Id == chosenId);
            return chosen is not null
                ? new MandateSignerResolution(MandateSignerResolutionStatus.Resolved, chosen)
                : new MandateSignerResolution(MandateSignerResolutionStatus.RequiereSeleccion, null);
        }

        if (candidates.Count == 0)
            return new MandateSignerResolution(MandateSignerResolutionStatus.NoConfigurado, null);

        if (candidates.Count == 1)
            return new MandateSignerResolution(MandateSignerResolutionStatus.Resolved, candidates[0]);

        // Varios mandatarios: cotejar por la cuenta de usuario que aprueba (§D9). Auto solo si el match
        // es ÚNICO (dos mandatarios con el mismo user_id sería un dato inconsistente ⇒ exige elegir).
        if (approvingUserId is { } uid && uid != Guid.Empty)
        {
            var matches = candidates.Where(c => c.UserId == uid).ToList();
            if (matches.Count == 1)
                return new MandateSignerResolution(MandateSignerResolutionStatus.Resolved, matches[0]);
        }

        return new MandateSignerResolution(MandateSignerResolutionStatus.RequiereSeleccion, null);
    }
}
