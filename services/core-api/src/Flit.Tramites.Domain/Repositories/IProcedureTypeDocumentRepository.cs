namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// Requisito documental de un tipo de trámite, con el código del tipo de documento ya resuelto
/// (FEATURE-08 / CFD-06). Vista de lectura del dominio sobre <c>tramites.procedure_document_requirements</c>.
/// </summary>
public sealed record ProcedureDocumentRequirementRecord(
    string DocumentTypeCode,
    bool IsRequired,
    bool IsDummy,
    string? ConditionGroup,
    int SortOrder);

/// <summary>Comando de upsert de un requisito documental: tipo de documento ya resuelto a su id.</summary>
public sealed record ProcedureDocumentRequirementUpsert(
    Guid DocumentTypeId,
    bool IsRequired,
    bool IsDummy,
    string? ConditionGroup,
    int SortOrder);

/// <summary>
/// Persiste y recupera los requisitos documentales por tipo de trámite para el configurador
/// dinámico (CFD-06). Catálogo GLOBAL sin <c>tenant_id</c>. El tipo de documento se referencia por
/// código y se resuelve contra <c>tramites.document_types</c>.
/// <para>Distinto del repositorio Admin <c>IProcedureDocumentRequirementRepository</c> (CRUD unitario
/// HU #10195): este expone reemplazo en bloque + <c>is_dummy</c>/<c>condition_group</c> para el perfil
/// de conformación.</para>
/// </summary>
public interface IProcedureTypeDocumentRepository
{
    /// <summary>Requisitos del tipo ordenados por <c>sort_order</c>, con el código de documento resuelto.</summary>
    Task<IReadOnlyList<ProcedureDocumentRequirementRecord>> ListByTypeAsync(
        Guid procedureTypeId, CancellationToken ct = default);

    /// <summary>Resuelve el id del tipo de documento por su código; null si no existe en el catálogo.</summary>
    Task<Guid?> ResolveDocumentTypeIdAsync(string code, CancellationToken ct = default);

    /// <summary>Reemplaza el conjunto completo de requisitos del tipo (borra e inserta).</summary>
    Task ReplaceRequirementsAsync(
        Guid procedureTypeId, IReadOnlyList<ProcedureDocumentRequirementUpsert> requirements, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
