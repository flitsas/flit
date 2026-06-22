namespace Flit.Admin.Domain.ProcedureSnapshots;

/// <summary>
/// Resultado de crear un trámite junto con su snapshot documental en una sola transacción
/// (HU #10197, AC1). Devuelve los valores generados por el repositorio que el endpoint
/// necesita para la respuesta 201.
/// </summary>
public sealed record CreatedProcedureInstance(
    Guid Id,
    string ReferenceNumber,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset SnapshotAt);
