namespace Flit.Admin.Domain.DocumentRequirements;

/// <summary>
/// Read model de una asociación trámite ↔ tipo de documento (HU #10195).
/// Proyección sobre <c>tramites.procedure_document_requirements</c> enriquecida con
/// los datos del tipo de documento asociado (join a <c>tramites.document_types</c>)
/// para el GET. La capa de aplicación traduce los nombres a la API en camelCase.
/// </summary>
public sealed class ProcedureDocumentRequirementItem
{
    /// <summary>Id de la asociación — <c>procedure_document_requirements.id</c>.</summary>
    public Guid Id { get; init; }

    /// <summary>Tipo de trámite — <c>procedure_document_requirements.procedure_type_id</c>.</summary>
    public Guid ProcedureTypeId { get; init; }

    /// <summary>Tipo de documento — <c>procedure_document_requirements.document_type_id</c>.</summary>
    public Guid DocumentTypeId { get; init; }

    /// <summary>Orden por defecto — <c>procedure_document_requirements.default_sort_order</c>.</summary>
    public short OrdenDefault { get; init; }

    /// <summary>Obligatoriedad en el trámite — <c>procedure_document_requirements.is_mandatory</c>.</summary>
    public bool Obligatorio { get; init; }

    /// <summary>Código del documento asociado — <c>document_types.code</c>.</summary>
    public string DocumentCode { get; init; } = string.Empty;

    /// <summary>Nombre del documento asociado — <c>document_types.name</c>.</summary>
    public string DocumentName { get; init; } = string.Empty;

    /// <summary>Estado activo del documento asociado — <c>document_types.is_active</c>.</summary>
    public bool DocumentIsActive { get; init; }
}
