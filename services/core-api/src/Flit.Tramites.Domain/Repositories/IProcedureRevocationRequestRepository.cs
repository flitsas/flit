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

    /// <summary>
    /// HU #12572 — fecha/hora en que el trámite llegó a <c>aprobado</c> por PRIMERA VEZ (la más
    /// ANTIGUA fila de <c>procedure_instance_status_history</c> con <c>to_status='aprobado'</c>, no la
    /// más reciente): es la base fija que <see cref="RevocationRequests.RevocationRequestGate"/> exige
    /// para <c>approvedAt</c> (AC2/AC5 — un reintento tras un rechazo del trámite no reinicia la
    /// ventana). <c>null</c> si no hay ninguna fila (el caller decide el fallback; no debería ocurrir
    /// para un trámite ya <c>aprobado</c>, pero esta consulta no lo asume).
    /// </summary>
    Task<DateTimeOffset?> GetFirstApprovedAtAsync(
        Guid tenantId, Guid procedureInstanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// HU #12572 — ventana de revocatoria configurada para el organismo de tránsito
    /// (<c>admin.transit_office_profiles.revocation_window_business_days</c>, HU #12567/#12568).
    /// Lectura CROSS-TENANT por <paramref name="transitOfficeId"/> (mismo criterio que
    /// <c>OtProfileRepository.GetByTransitOfficeAsync</c>: el perfil es del organismo, no del tenant
    /// cliente que lo consulta). <c>null</c> si el organismo no tiene perfil configurado = sin límite
    /// (mismo significado que "sin configurar" para <see cref="RevocationRequestGate"/>,
    /// AC1/AC5).
    /// </summary>
    Task<int?> GetRevocationWindowBusinessDaysAsync(
        Guid transitOfficeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// HU #12578 (Feature #12565) — listado paginado/filtrado de solicitudes de revocatoria del propio
    /// <paramref name="tenantId"/> (lado gestor, Administrador de compañía dueño del trámite), UNA fila
    /// por intento —igual que la tabla—, más recientes primero. Aislamiento por tenant en el
    /// <c>WHERE</c> (mismo patrón que el resto del repositorio); la RLS de la tabla es defensa en
    /// profundidad, no hace falta desactivarla (a diferencia de <see cref="ListForTransitOfficeAsync"/>).
    /// </summary>
    Task<RevocationRequestListPage> ListForTenantAsync(
        Guid tenantId, RevocationRequestListFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// HU #12578 — misma consulta que <see cref="ListForTenantAsync"/> pero del lado OT: CROSS-TENANT
    /// (todos los trámites del organismo <paramref name="transitOfficeId"/>, sin importar qué compañía
    /// cliente los radicó — el <paramref name="transitOfficeId"/> ya viene resuelto por el caller, mismo
    /// mecanismo que <c>OtClientProcedureRepository</c>). La RLS de
    /// <c>tramites.procedure_revocation_requests</c> aísla por <c>tenant_id</c> del CLIENTE y el OT no
    /// tiene uno propio en esa tabla, así que la implementación desactiva RLS localmente
    /// (<c>SET LOCAL row_security = off</c> dentro de una transacción), igual que el resto de lecturas
    /// cross-tenant de la bandeja OT.
    /// </summary>
    Task<RevocationRequestListPage> ListForTransitOfficeAsync(
        Guid transitOfficeId, RevocationRequestListFilter filter, CancellationToken cancellationToken = default);
}
