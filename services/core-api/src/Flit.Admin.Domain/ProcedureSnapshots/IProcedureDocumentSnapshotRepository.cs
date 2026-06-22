namespace Flit.Admin.Domain.ProcedureSnapshots;

/// <summary>
/// Lectura del snapshot documental de un trámite (HU #10197, AC2/AC4). La escritura se
/// realiza junto con la instancia en
/// <see cref="IProcedureInstanceRepository.CreateWithSnapshotAsync"/> (misma transacción).
/// </summary>
public interface IProcedureDocumentSnapshotRepository
{
    /// <summary>
    /// Devuelve el snapshot asociado a una instancia de trámite, o null si el trámite no
    /// tiene snapshot (trámite legacy → 404 en el GET).
    /// </summary>
    Task<ProcedureDocumentSnapshotRecord?> GetByProcedureInstanceIdAsync(
        Guid procedureInstanceId,
        CancellationToken cancellationToken = default);
}
