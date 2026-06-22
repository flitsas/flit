namespace Flit.Admin.Application.DocumentOrderOverrides.GetResolvedDocumentMatrix;

/// <summary>Desenlace de la consulta de la matriz resuelta (AC3/AC4).</summary>
public enum GetResolvedDocumentMatrixOutcome
{
    /// <summary>Resuelta correctamente → HTTP 200 (la lista puede venir vacía).</summary>
    Resolved,

    /// <summary>El tipo de trámite no existe → HTTP 404.</summary>
    ProcedureTypeNotFound,
}

/// <summary>
/// Resultado de la resolución: el <see cref="Outcome"/> determina el status HTTP y
/// <see cref="Data"/> trae las filas resueltas (200), ordenadas por orden resuelto asc.
/// </summary>
public sealed class GetResolvedDocumentMatrixResult
{
    private GetResolvedDocumentMatrixResult(
        GetResolvedDocumentMatrixOutcome outcome,
        IReadOnlyList<ResolvedDocumentMatrixResponse> data)
    {
        Outcome = outcome;
        Data = data;
    }

    public GetResolvedDocumentMatrixOutcome Outcome { get; }

    public IReadOnlyList<ResolvedDocumentMatrixResponse> Data { get; }

    public static GetResolvedDocumentMatrixResult Resolved(
        IReadOnlyList<ResolvedDocumentMatrixResponse> data) =>
        new(GetResolvedDocumentMatrixOutcome.Resolved, data);

    public static GetResolvedDocumentMatrixResult ProcedureTypeNotFound { get; } =
        new(GetResolvedDocumentMatrixOutcome.ProcedureTypeNotFound, []);
}
