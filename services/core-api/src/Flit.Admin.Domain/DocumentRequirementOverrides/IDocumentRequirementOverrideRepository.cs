namespace Flit.Admin.Domain.DocumentRequirementOverrides;

/// <summary>
/// Repositorio de overrides de obligatoriedad documental por OT (HU #10198). La
/// implementación (Infrastructure) opera sobre <c>tramites.document_requirement_overrides</c>
/// con queries parametrizadas EF LINQ. El "limpiar override" es un borrado <b>físico</b>.
/// </summary>
public interface IDocumentRequirementOverrideRepository
{
    /// <summary>
    /// Inserta o actualiza el estado de obligatoriedad de la tupla (trámite, documento, OT).
    /// Devuelve el read model enriquecido.
    /// </summary>
    Task<DocumentRequirementOverrideItem> UpsertAsync(
        Guid procedureTypeId,
        Guid documentTypeId,
        Guid transitOfficeId,
        string requirementState,
        Guid? actor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Borra el override de la tupla (trámite, documento, OT) — vuelve a heredar el default.
    /// Devuelve false si no había override.
    /// </summary>
    Task<bool> ClearAsync(
        Guid procedureTypeId,
        Guid documentTypeId,
        Guid transitOfficeId,
        CancellationToken cancellationToken = default);

    /// <summary>Lista los overrides de un trámite para un OT (enriquecidos).</summary>
    Task<IReadOnlyList<DocumentRequirementOverrideItem>> ListByOtAsync(
        Guid procedureTypeId,
        Guid transitOfficeId,
        CancellationToken cancellationToken = default);
}
