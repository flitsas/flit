using Flit.Admin.Domain.Common;

namespace Flit.Admin.Domain.DocumentTypes;

/// <summary>
/// Repositorio del catálogo maestro de tipos de documento (HU #10193, RF01–RF04).
/// La implementación (Infrastructure) opera sobre <c>tramites.document_types</c>
/// (catálogo global SuperAdmin, sin RLS) con queries parametrizadas EF LINQ.
/// </summary>
public interface IDocumentTypeRepository
{
    /// <summary>Crea un tipo de documento activo y devuelve el read model resultante (AC1).</summary>
    Task<DocumentTypeListItem> CreateAsync(
        string code,
        string name,
        string? description,
        Guid? createdBy,
        CancellationToken cancellationToken = default);

    /// <summary>Listado paginado ordenado por nombre ascendente (AC2).</summary>
    Task<PagedResult<DocumentTypeListItem>> ListAsync(
        DocumentTypeListFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>Devuelve el tipo de documento por id (activo o inactivo), o null si no existe.</summary>
    Task<DocumentTypeListItem?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>Actualiza un tipo de documento existente; devuelve null si no existe (AC3).</summary>
    Task<DocumentTypeListItem?> UpdateAsync(
        Guid id,
        string code,
        string name,
        string? description,
        Guid? updatedBy,
        CancellationToken cancellationToken = default);

    /// <summary>Soft-delete: marca <c>is_active = false</c>; devuelve false si no existe (AC4).</summary>
    Task<bool> SoftDeleteAsync(
        Guid id,
        Guid? updatedBy,
        CancellationToken cancellationToken = default);

    /// <summary>Reactivación: marca <c>is_active = true</c>; devuelve false si no existe.</summary>
    Task<bool> ReactivateAsync(
        Guid id,
        Guid? updatedBy,
        CancellationToken cancellationToken = default);

    /// <summary>True si el tipo de documento está referenciado en procedure_document_requirements (AC6).</summary>
    Task<bool> HasActiveAssociationsAsync(
        Guid documentTypeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tipos de trámite que referencian el documento, para enriquecer el 409 del soft-delete
    /// con sus nombres. Best-effort: si algún trámite no resuelve (p. ej. catálogo sin la fila),
    /// simplemente no aparece; la decisión de bloquear sigue dependiendo de
    /// <see cref="HasActiveAssociationsAsync"/>.
    /// </summary>
    Task<IReadOnlyList<DocumentTypeAssociationRef>> GetAssociatedProcedureTypesAsync(
        Guid documentTypeId,
        CancellationToken cancellationToken = default);

    /// <summary>True si el código ya existe (unicidad global); <paramref name="excludeId"/> excluye el propio en update.</summary>
    Task<bool> CodeExistsAsync(
        string code,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default);
}
