namespace Flit.Modules.Quipux.Domain.Consola;

/// <summary>
/// Acceso de LECTURA y OPERACIÓN de la consola de cola Quipux del SuperAdmin (HU #10774). Es un
/// puerto distinto de <c>IQuipuxSubmissionRepository</c> (el de los workers, orientado a claim
/// exclusivo e idempotencia): aquí no hay <c>FOR UPDATE SKIP LOCKED</c> ni leases, sino un listado
/// paginado por secretaría destino y dos acciones manuales acotadas.
///
/// <para>Toda operación va SCOPED a una secretaría destino (<c>transitOfficeId</c>): la submission
/// solo es visible/operable desde la página del OT al que se radica su trámite
/// (<c>procedure_instances.transit_office_id</c>). Una submission que no pertenece a ese OT se
/// reporta como <see cref="QuipuxManualOpStatus.NotFound"/> —no se filtra información de otro OT—.</para>
/// </summary>
public interface IQuipuxSubmissionConsoleRepository
{
    /// <summary>
    /// Cola (activa + histórico) de la secretaría destino <paramref name="transitOfficeId"/>,
    /// ordenada por <c>created_at</c> descendente, paginada. Incluye todos los estados: el
    /// histórico (aprobado/rechazado/fallido) da la trazabilidad completa por trámite.
    /// </summary>
    Task<QuipuxSubmissionQueuePage> ListByTransitOfficeAsync(
        Guid transitOfficeId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-encola una radicación <c>fallido</c> (dead-letter): la lleva a <c>pendiente</c> y resetea
    /// el presupuesto de intentos y el lease, de modo que el <c>QuipuxRegisterProcessor</c> vuelva a
    /// reclamarla. Falla con <see cref="QuipuxManualOpStatus.InvalidState"/> si no está en
    /// <c>fallido</c>, y con <see cref="QuipuxManualOpStatus.Conflict"/> si el trámite ya tiene otra
    /// radicación viva (el índice <c>uq_quipux_submissions_activa</c> no permite dos).
    /// </summary>
    Task<QuipuxManualOpResult> RetryAsync(
        Guid submissionId,
        Guid transitOfficeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancela una radicación <c>pendiente</c> antes de que se radique: la lleva a <c>fallido</c>
    /// (terminal) con un motivo. Falla con <see cref="QuipuxManualOpStatus.InvalidState"/> si ya no
    /// está en <c>pendiente</c> (p. ej. un worker la reclamó y la registró entre la carga de la
    /// pantalla y el clic): la transición se guarda con un filtro por estado, no a ciegas.
    /// </summary>
    Task<QuipuxManualOpResult> CancelAsync(
        Guid submissionId,
        Guid transitOfficeId,
        CancellationToken cancellationToken = default);
}

/// <summary>Desenlace de una acción manual sobre una submission, con lo que el caso de uso necesita para auditar.</summary>
/// <param name="Status">Resultado de la operación.</param>
/// <param name="TenantId">Tenant de la submission afectada (para escribir la bitácora con RLS). Null si no se afectó.</param>
/// <param name="PreviousStatus">Estado anterior a la transición, para el <c>detail</c> sanitizado del evento.</param>
/// <param name="PreviousAttempts">Intentos previos (solo relevante en el reintento).</param>
public sealed record QuipuxManualOpResult(
    QuipuxManualOpStatus Status,
    Guid? TenantId = null,
    string? PreviousStatus = null,
    int? PreviousAttempts = null);

/// <summary>Resultados posibles de una acción manual de la consola de cola.</summary>
public enum QuipuxManualOpStatus
{
    /// <summary>La transición se aplicó.</summary>
    Ok,

    /// <summary>No existe una submission con ese id en la secretaría indicada.</summary>
    NotFound,

    /// <summary>La submission no está en un estado que admita esta acción.</summary>
    InvalidState,

    /// <summary>El trámite ya tiene otra radicación viva; re-encolar violaría la unicidad.</summary>
    Conflict,
}
