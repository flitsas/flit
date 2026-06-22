using Flit.Admin.Domain.ProcedureSnapshots;

namespace Flit.Admin.Application.ProcedureInstances;

/// <summary>
/// Respuesta pública del alta de un trámite (HU #10197, AC1). Confirma el id, el número de
/// referencia, el instante en que se capturó el snapshot documental y cuántos documentos
/// quedaron congelados.
/// </summary>
public sealed record ProcedureInstanceResponse(
    Guid Id,
    string ReferenceNumber,
    string Status,
    DateTimeOffset SnapshotAt,
    int DocumentCount)
{
    public static ProcedureInstanceResponse From(CreatedProcedureInstance created, int documentCount)
    {
        ArgumentNullException.ThrowIfNull(created);

        return new ProcedureInstanceResponse(
            created.Id,
            created.ReferenceNumber,
            created.Status,
            created.SnapshotAt,
            documentCount);
    }
}
