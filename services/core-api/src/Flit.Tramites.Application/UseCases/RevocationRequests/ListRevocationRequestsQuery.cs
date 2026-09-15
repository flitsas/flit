using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.RevocationRequests;

namespace Flit.Tramites.Application.UseCases.RevocationRequests;

/// <summary>
/// HU #12578 (Feature #12565) — pedido del listado dedicado "Revocatorias" del lado gestor
/// (Administrador de compañía, tenant-scoped a su propia compañía). Mismos nombres de filtro que el
/// query equivalente del lado OT (<c>Flit.Api.UseCases.RevocationRequests.ListOtRevocationRequestsQuery</c>)
/// a propósito: el frontend consume ambos endpoints con el mismo shape.
/// </summary>
public sealed record ListRevocationRequestsQuery(
    Guid TenantId,
    IReadOnlyList<string>? Statuses,
    DateTimeOffset? RequestedFrom,
    DateTimeOffset? RequestedTo,
    Guid? TransitOfficeId,
    int? Skip,
    int? Take);

/// <summary>
/// HU #12578 — fila del listado dedicado, compartida por gestor y OT (misma forma para las dos
/// respuestas: simplifica el consumo desde el frontend).
/// </summary>
public sealed record RevocationRequestListItemDto(
    Guid RevocationRequestId,
    Guid ProcedureInstanceId,
    string ReferenceNumber,
    string? Placa,
    Guid? TransitOfficeId,
    string? TransitOfficeName,
    string Status,
    int AttemptNumber,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DecidedAt)
{
    /// <summary>
    /// Público (no <c>internal</c>): el listado del lado OT (<c>Flit.Api.UseCases.RevocationRequests.ListOtRevocationRequestsHandler</c>)
    /// vive en OTRO proyecto y reutiliza este mapeo para que las dos respuestas tengan EXACTAMENTE la
    /// misma forma.
    /// </summary>
    public static RevocationRequestListItemDto From(RevocationRequestListItem item) => new(
        item.RevocationRequestId,
        item.ProcedureInstanceId,
        item.ReferenceNumber,
        item.Placa,
        item.TransitOfficeId,
        item.TransitOfficeName,
        item.Status,
        item.AttemptNumber,
        item.RequestedAt,
        item.DecidedAt);
}

/// <summary>HU #12578 — página de resultados ya paginada, con el eco de <c>skip</c>/<c>take</c> aplicados.</summary>
public sealed record RevocationRequestListResult(
    IReadOnlyList<RevocationRequestListItemDto> Items,
    long Total,
    int Skip,
    int Take);

/// <summary>
/// Normalización de paginación COMPARTIDA por los dos listados dedicados de revocatorias (gestor y OT,
/// HU #12578): mismos defaults/tope para que las dos respuestas se comporten igual ante los mismos
/// parámetros. <c>skip</c>/<c>take</c> (no <c>page</c>/<c>pageSize</c>) para alinear con el listado
/// filtrado de trámites (<c>ProcedureInstanceListRequest</c>), el pariente más cercano de este
/// endpoint.
/// </summary>
public static class RevocationRequestListPaging
{
    public const int DefaultTake = 20;
    public const int MaxTake = 100;

    /// <summary><c>null</c> o <c>&lt; 1</c> ⇒ <see cref="DefaultTake"/>; por encima de <see cref="MaxTake"/> ⇒ tope.</summary>
    public static int NormalizeTake(int? take) =>
        take is null or < 1 ? DefaultTake : Math.Min(take.Value, MaxTake);

    /// <summary><c>null</c> o negativo ⇒ 0.</summary>
    public static int NormalizeSkip(int? skip) =>
        skip is null or < 0 ? 0 : skip.Value;
}

/// <summary>
/// «Listar solicitudes de revocatoria» del lado gestor (HU #12578, Feature #12565): alimenta la vista
/// dedicada "Revocatorias" para el Administrador de compañía dueño de los trámites. Delega el
/// <c>WHERE</c>/<c>JOIN</c>/paginación al repositorio (SQL); aquí solo se normaliza la paginación y se
/// mapea la fila del dominio al DTO de transporte.
/// </summary>
public sealed class ListRevocationRequestsHandler(IProcedureRevocationRequestRepository repo)
{
    public async Task<RevocationRequestListResult> HandleAsync(
        ListRevocationRequestsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var take = RevocationRequestListPaging.NormalizeTake(query.Take);
        var skip = RevocationRequestListPaging.NormalizeSkip(query.Skip);

        var filter = new RevocationRequestListFilter
        {
            Statuses = query.Statuses,
            RequestedFrom = query.RequestedFrom,
            RequestedTo = query.RequestedTo,
            TransitOfficeId = query.TransitOfficeId,
            Skip = skip,
            Take = take,
        };

        var page = await repo.ListForTenantAsync(query.TenantId, filter, cancellationToken)
            .ConfigureAwait(false);

        return new RevocationRequestListResult(
            page.Items.Select(RevocationRequestListItemDto.From).ToList(),
            page.TotalCount,
            skip,
            take);
    }
}
