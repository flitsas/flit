namespace Flit.Admin.Application.ProcedureSnapshots.GetProcedureDocumentRequirements;

/// <summary>
/// Petición de los documentos requeridos del snapshot de un trámite (HU #10197, AC2/AC4).
/// </summary>
public sealed class GetProcedureDocumentRequirementsQuery
{
    public required Guid TramiteId { get; init; }
}
