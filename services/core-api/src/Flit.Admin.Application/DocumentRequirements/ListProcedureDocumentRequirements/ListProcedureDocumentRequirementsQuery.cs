namespace Flit.Admin.Application.DocumentRequirements.ListProcedureDocumentRequirements;

/// <summary>
/// Petición del listado de documentos asociados a un tipo de trámite (HU #10195, AC2).
/// </summary>
public sealed class ListProcedureDocumentRequirementsQuery
{
    /// <summary>Tipo de trámite cuyo catálogo de documentos se consulta (obligatorio).</summary>
    public required Guid ProcedureTypeId { get; init; }
}
