namespace Flit.Admin.Domain.DocumentRequirements;

/// <summary>
/// Consulta de solo lectura sobre el catálogo de tipos de trámite
/// (<c>tramites.procedure_types</c>, Feature #10116). HU #10195 únicamente necesita
/// comprobar la existencia de un trámite al asociar documentos; el CRUD de tipos de
/// trámite pertenece a otra HU.
/// </summary>
public interface IProcedureTypeCatalog
{
    /// <summary>True si existe un tipo de trámite con el id indicado.</summary>
    Task<bool> ExistsAsync(
        Guid procedureTypeId,
        CancellationToken cancellationToken = default);
}
