namespace Flit.Tramites.Domain.RevocationRequests;

/// <summary>
/// HU #12578 (Feature #12565) — filtro del listado dedicado de solicitudes de revocatoria (vista
/// "Revocatorias"), compartido por las dos lecturas del repositorio
/// (<see cref="Repositories.IProcedureRevocationRequestRepository.ListForTenantAsync"/> del lado
/// gestor y <see cref="Repositories.IProcedureRevocationRequestRepository.ListForTransitOfficeAsync"/>
/// del lado OT) para que ambas acepten EXACTAMENTE los mismos criterios y el frontend no tenga que
/// mapear dos formas distintas de pedir lo mismo.
/// </summary>
public sealed class RevocationRequestListFilter
{
    /// <summary>
    /// Sub-estados a incluir (OR) — valores de <see cref="ProcedureRevocationRequestStatus"/>.
    /// <c>null</c> o vacío = todos los estados (activos e históricos).
    /// </summary>
    public IReadOnlyList<string>? Statuses { get; init; }

    /// <summary>Filtra <c>requested_at &gt;= RequestedFrom</c> (inclusive, UTC).</summary>
    public DateTimeOffset? RequestedFrom { get; init; }

    /// <summary>Filtra <c>requested_at &lt;= RequestedTo</c> (inclusive, UTC).</summary>
    public DateTimeOffset? RequestedTo { get; init; }

    /// <summary>
    /// Organismo de tránsito del trámite. Solo tiene efecto en <c>ListForTenantAsync</c> (lado
    /// gestor, que puede tener trámites repartidos entre varios OT): en <c>ListForTransitOfficeAsync</c>
    /// (lado OT) el alcance YA es un único organismo — resuelto ANTES de llamar al repositorio, igual
    /// que el resto de la bandeja OT (<c>OtClientProcedureRepository</c>) — así que este campo se
    /// ignora ahí.
    /// </summary>
    public Guid? TransitOfficeId { get; init; }

    /// <summary>Filas a saltar (paginación 0-based, mismo estilo que <c>ProcedureInstanceListRequest</c>).</summary>
    public int Skip { get; init; }

    /// <summary>Filas a devolver. La normalización (default/tope) vive en el handler de aplicación.</summary>
    public int Take { get; init; } = 20;
}

/// <summary>
/// HU #12578 — una fila del listado dedicado: UN intento de solicitud de revocatoria
/// (<see cref="ProcedureRevocationRequest"/>) enriquecido con los datos del trámite que hacen falta
/// para pintar la fila sin una segunda consulta (radicado, placa, organismo).
/// </summary>
public sealed record RevocationRequestListItem(
    Guid RevocationRequestId,
    Guid ProcedureInstanceId,
    string ReferenceNumber,
    string? Placa,
    Guid? TransitOfficeId,
    string? TransitOfficeName,
    string Status,
    int AttemptNumber,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DecidedAt);

/// <summary>HU #12578 — página de resultados de <see cref="RevocationRequestListItem"/> ya paginada/ordenada.</summary>
public sealed record RevocationRequestListPage(IReadOnlyList<RevocationRequestListItem> Items, long TotalCount)
{
    public static RevocationRequestListPage Empty { get; } = new([], 0);
}
