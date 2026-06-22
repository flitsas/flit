namespace Flit.Admin.Domain.DocumentRequirementOverrides;

/// <summary>
/// Read model de un override de obligatoriedad por OT (HU #10198). Proyección sobre
/// <c>tramites.document_requirement_overrides</c> enriquecida con los datos del tipo de
/// documento (join a <c>tramites.document_types</c>).
/// </summary>
public sealed class DocumentRequirementOverrideItem
{
    public Guid Id { get; init; }

    public Guid ProcedureTypeId { get; init; }

    public Guid DocumentTypeId { get; init; }

    public Guid TransitOfficeId { get; init; }

    /// <summary><c>REQUIRED</c> | <c>OPTIONAL</c> | <c>NOT_APPLICABLE</c>.</summary>
    public string RequirementState { get; init; } = string.Empty;

    /// <summary>Código del documento — <c>document_types.code</c>.</summary>
    public string DocumentCode { get; init; } = string.Empty;

    /// <summary>Nombre del documento — <c>document_types.name</c>.</summary>
    public string DocumentName { get; init; } = string.Empty;
}
