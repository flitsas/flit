namespace Flit.Admin.Application.DocumentRequirements.CreateProcedureDocumentRequirement;

/// <summary>
/// Payload de alta de una asociación trámite ↔ documento (HU #10195, AC1):
/// <c>{ procedureTypeId, documentTypeId, ordenDefault, obligatorio }</c>.
/// </summary>
/// <param name="ProcedureTypeId">Tipo de trámite (debe existir en <c>procedure_types</c>).</param>
/// <param name="DocumentTypeId">Tipo de documento (debe existir y estar activo).</param>
/// <param name="OrdenDefault">Orden por defecto (smallint ≥ 0).</param>
/// <param name="Obligatorio">Si el documento es obligatorio en el trámite.</param>
public sealed record CreateProcedureDocumentRequirementRequest(
    Guid ProcedureTypeId,
    Guid DocumentTypeId,
    int? OrdenDefault,
    bool? Obligatorio);
