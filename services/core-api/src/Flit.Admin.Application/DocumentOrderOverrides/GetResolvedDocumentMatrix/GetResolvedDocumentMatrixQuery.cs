namespace Flit.Admin.Application.DocumentOrderOverrides.GetResolvedDocumentMatrix;

/// <summary>
/// Petición de la matriz documental resuelta (HU #10196, AC3/AC4 / RF18; RF22).
/// <see cref="TransitOfficeId"/> es opcional: sin él se resuelve el orden base del trámite.
/// </summary>
public sealed class GetResolvedDocumentMatrixQuery
{
    public required Guid ProcedureTypeId { get; init; }

    public Guid? TransitOfficeId { get; init; }
}
