using Flit.Admin.Domain.ProcedureSnapshots;

namespace Flit.Admin.Application.ProcedureSnapshots.GetProcedureDocumentRequirements;

/// <summary>
/// Caso de uso de lectura de los documentos requeridos de un trámite desde su snapshot
/// inmutable (HU #10197, AC2 / AC4 / RF19).
///
/// Flujo: (1) el trámite debe existir → 404; (2) debe tener snapshot → 404 (trámite legacy);
/// (3) devuelve los documentos <b>del JSON congelado</b> —no de la matriz live— ordenados
/// por <c>ordenResuelto</c> asc (desempate <c>documentTypeId</c>). Los cambios posteriores
/// en catálogo u overrides no afectan la respuesta.
/// </summary>
public sealed class GetProcedureDocumentRequirementsHandler
{
    private readonly IProcedureInstanceRepository _instances;
    private readonly IProcedureDocumentSnapshotRepository _snapshots;

    public GetProcedureDocumentRequirementsHandler(
        IProcedureInstanceRepository instances,
        IProcedureDocumentSnapshotRepository snapshots)
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
    }

    public async Task<GetProcedureDocumentRequirementsResult> HandleAsync(
        GetProcedureDocumentRequirementsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await _instances.ExistsAsync(query.TramiteId, cancellationToken).ConfigureAwait(false))
        {
            return GetProcedureDocumentRequirementsResult.NotFound;
        }

        var snapshot = await _snapshots
            .GetByProcedureInstanceIdAsync(query.TramiteId, cancellationToken)
            .ConfigureAwait(false);
        if (snapshot is null)
        {
            return GetProcedureDocumentRequirementsResult.NotFound;
        }

        var payload = SnapshotJson.Deserialize(snapshot.SnapshotJson);
        var documentos = payload?.Documentos ?? [];

        var data = documentos
            .OrderBy(d => d.OrdenResuelto)
            .ThenBy(d => d.DocumentTypeId)
            .Select(ProcedureDocumentRequirementSnapshotResponse.From)
            .ToList();

        return GetProcedureDocumentRequirementsResult.Found(data);
    }
}
