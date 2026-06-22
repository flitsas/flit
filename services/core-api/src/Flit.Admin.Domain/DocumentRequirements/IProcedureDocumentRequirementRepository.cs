namespace Flit.Admin.Domain.DocumentRequirements;

/// <summary>
/// Repositorio de la asociación trámite ↔ tipo de documento (HU #10195, RF05–RF08).
/// La implementación (Infrastructure) opera sobre
/// <c>tramites.procedure_document_requirements</c> con queries parametrizadas EF LINQ.
/// El DELETE es <b>físico</b> (no soft-delete).
/// </summary>
public interface IProcedureDocumentRequirementRepository
{
    /// <summary>Crea la asociación y devuelve el read model enriquecido (AC1).</summary>
    Task<ProcedureDocumentRequirementItem> CreateAsync(
        Guid procedureTypeId,
        Guid documentTypeId,
        short ordenDefault,
        bool obligatorio,
        Guid? createdBy,
        CancellationToken cancellationToken = default);

    /// <summary>Lista las asociaciones de un trámite ordenadas por orden default asc (AC2).</summary>
    Task<IReadOnlyList<ProcedureDocumentRequirementItem>> ListByProcedureTypeAsync(
        Guid procedureTypeId,
        CancellationToken cancellationToken = default);

    /// <summary>Devuelve la asociación por id (enriquecida), o null si no existe.</summary>
    Task<ProcedureDocumentRequirementItem?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>Actualiza orden y obligatoriedad; devuelve null si no existe (AC3).</summary>
    Task<ProcedureDocumentRequirementItem?> UpdateAsync(
        Guid id,
        short ordenDefault,
        bool obligatorio,
        CancellationToken cancellationToken = default);

    /// <summary>Borrado físico; devuelve false si no existe (AC4).</summary>
    Task<bool> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>True si ya existe el par (trámite, documento) — unicidad de la asociación.</summary>
    Task<bool> ExistsPairAsync(
        Guid procedureTypeId,
        Guid documentTypeId,
        CancellationToken cancellationToken = default);
}
