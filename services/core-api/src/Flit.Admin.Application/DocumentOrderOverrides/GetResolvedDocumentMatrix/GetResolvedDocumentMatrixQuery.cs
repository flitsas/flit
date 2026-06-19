namespace Flit.Admin.Application.DocumentOrderOverrides.GetResolvedDocumentMatrix;

/// <summary>
/// Petición de la matriz documental resuelta (HU #10196, AC3/AC4 / RF18).
/// <see cref="TransitOfficeId"/> y <see cref="ClienteId"/> son opcionales: si no vienen,
/// ese nivel de precedencia se omite.
/// </summary>
public sealed class GetResolvedDocumentMatrixQuery
{
    public required Guid ProcedureTypeId { get; init; }

    public Guid? TransitOfficeId { get; init; }

    public Guid? ClienteId { get; init; }
}
