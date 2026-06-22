namespace Flit.Admin.Application.ProcedureSnapshots.GetProcedureDocumentRequirements;

/// <summary>Desenlace de la consulta de documentos del snapshot (AC2/AC4).</summary>
public enum GetProcedureDocumentRequirementsOutcome
{
    /// <summary>Snapshot encontrado → HTTP 200 (la lista puede venir vacía).</summary>
    Found,

    /// <summary>El trámite no existe o no tiene snapshot → HTTP 404.</summary>
    NotFound,
}

/// <summary>
/// Resultado de la consulta: el <see cref="Outcome"/> determina el status HTTP y
/// <see cref="Data"/> trae los documentos del snapshot ordenados por orden resuelto asc.
/// </summary>
public sealed class GetProcedureDocumentRequirementsResult
{
    private GetProcedureDocumentRequirementsResult(
        GetProcedureDocumentRequirementsOutcome outcome,
        IReadOnlyList<ProcedureDocumentRequirementSnapshotResponse> data)
    {
        Outcome = outcome;
        Data = data;
    }

    public GetProcedureDocumentRequirementsOutcome Outcome { get; }

    public IReadOnlyList<ProcedureDocumentRequirementSnapshotResponse> Data { get; }

    public static GetProcedureDocumentRequirementsResult Found(
        IReadOnlyList<ProcedureDocumentRequirementSnapshotResponse> data) =>
        new(GetProcedureDocumentRequirementsOutcome.Found, data);

    public static GetProcedureDocumentRequirementsResult NotFound { get; } =
        new(GetProcedureDocumentRequirementsOutcome.NotFound, []);
}
