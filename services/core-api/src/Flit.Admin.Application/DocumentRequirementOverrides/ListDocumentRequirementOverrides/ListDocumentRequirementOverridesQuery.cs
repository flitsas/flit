namespace Flit.Admin.Application.DocumentRequirementOverrides.ListDocumentRequirementOverrides;

/// <summary>Consulta de los overrides de obligatoriedad de un trámite para un OT (HU #10198).</summary>
public sealed class ListDocumentRequirementOverridesQuery
{
    public required Guid ProcedureTypeId { get; init; }

    public required Guid TransitOfficeId { get; init; }
}
