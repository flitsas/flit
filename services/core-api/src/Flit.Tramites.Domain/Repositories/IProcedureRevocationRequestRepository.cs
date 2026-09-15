using Flit.Tramites.Domain.RevocationRequests;

namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// HU #12571 (Feature #12565) — persistencia mínima de <see cref="ProcedureRevocationRequest"/> para
/// alimentar <see cref="RevocationRequestGate"/> y crear la fila del intento. Aislamiento por tenant en
/// el <c>WHERE</c> (mismo patrón que <see cref="IProcedureInstanceRepository"/>); la RLS de la tabla es
/// defensa en profundidad.
/// </summary>
public interface IProcedureRevocationRequestRepository
{
    /// <summary>
    /// Solicitud ACTIVA (<c>solicitada</c>/<c>en_revision</c>) del trámite, o <c>null</c> si no hay
    /// ninguna (AC1/AC4). A lo sumo puede haber una, por el índice único parcial de BD.
    /// </summary>
    Task<ProcedureRevocationRequest?> FindActiveAsync(
        Guid tenantId, Guid procedureInstanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Última solicitud del trámite (cualquier estado, la más reciente por <c>requested_at</c>), o
    /// <c>null</c> si nunca se ha solicitado una revocatoria. AC5 la usa para decidir el reintento tras
    /// un rechazo sin depender de <see cref="FindActiveAsync"/> (que no la vería por no estar activa).
    /// </summary>
    Task<ProcedureRevocationRequest?> FindLastAsync(
        Guid tenantId, Guid procedureInstanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Siguiente <c>attempt_number</c> para el trámite: 1 si nunca se ha solicitado, o el máximo existente
    /// + 1. Monotónico y nunca reutiliza un número (AC1/AC5).
    /// </summary>
    Task<int> GetNextAttemptNumberAsync(
        Guid tenantId, Guid procedureInstanceId, CancellationToken cancellationToken = default);

    void Add(ProcedureRevocationRequest request);

    /// <summary>
    /// Persiste los cambios. Traduce el <c>23505</c> del índice único parcial
    /// <c>uq_procedure_revocation_requests_active_per_instance</c> a
    /// <see cref="ActiveRevocationRequestExistsException"/> (AC4, checklist §B12: no reimplementar la
    /// unicidad en memoria, la BD es la fuente de verdad ante una carrera concurrente).
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
