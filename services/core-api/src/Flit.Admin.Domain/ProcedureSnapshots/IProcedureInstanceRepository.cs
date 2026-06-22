namespace Flit.Admin.Domain.ProcedureSnapshots;

/// <summary>
/// Repositorio de instancias de trámite (HU #10197, RF19). La implementación
/// (Infrastructure) opera sobre <c>tramites.procedure_instances</c> con EF LINQ
/// parametrizado.
///
/// <see cref="CreateWithSnapshotAsync"/> persiste la instancia y su snapshot documental
/// en una <b>única unidad de trabajo</b> (un solo <c>SaveChangesAsync</c>): si la escritura
/// del snapshot falla, ningún registro queda persistido (AC3).
/// </summary>
public interface IProcedureInstanceRepository
{
    /// <summary>True si existe la instancia de trámite (404 del GET si no).</summary>
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Devuelve la instancia por id, o null si no existe.</summary>
    Task<ProcedureInstanceRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>True si el número de referencia ya existe para el tenant — unicidad (422).</summary>
    Task<bool> ReferenceNumberExistsAsync(
        Guid tenantId,
        string referenceNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea el trámite (estado <c>draft</c>) y su snapshot documental atómicamente. El
    /// <paramref name="snapshotJson"/> es la matriz resuelta ya serializada; un fallo al
    /// persistir revierte toda la operación (AC3).
    /// </summary>
    Task<CreatedProcedureInstance> CreateWithSnapshotAsync(
        NewProcedureInstance instance,
        string snapshotJson,
        Guid? snapshotBy,
        CancellationToken cancellationToken = default);
}
