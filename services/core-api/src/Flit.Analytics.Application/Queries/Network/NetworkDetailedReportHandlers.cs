using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using Flit.Queries.Domain.Tenancy;

namespace Flit.Analytics.Application.Queries.Network;

/// <summary>
/// Filtros del reporte de red tal como llegan de la petición (HU #12360): los MISMOS de
/// <c>GET /api/v1/detailed-report/procedures</c> más <see cref="ChildTenantId"/>. Ningún campo aquí es
/// fuente de alcance: <see cref="ChildTenantId"/> solo acota el alcance que resuelve el servidor.
/// </summary>
public sealed record NetworkDetailedReportRequest(
    Guid? ChildTenantId,
    DateOnly From,
    DateOnly To,
    Guid? TransitOfficeId = null,
    Guid? ProcedureTypeId = null,
    string? Category = null,
    string? Status = null,
    string? ReferenceNumber = null,
    string? PersonDocument = null,
    string? PersonName = null,
    bool? HasTransformation = null,
    bool? IsLeasing = null);

/// <summary>
/// HU #12360 (Feature #12257, épica #12235) — ÚNICA función de resolución del reporte de red, compartida
/// por el listado y la exportación (AC5): el conjunto de clientes sale de
/// <see cref="NetworkAnalyticsScope.Resolve"/> (alcance del servidor ∩ <c>childTenantId</c>; ajeno ⇒
/// <see cref="NetworkScopePolicy.ChildOutOfScope"/> sin consulta, AC3/AC4) y los filtros se normalizan
/// EXACTAMENTE como en <c>GetDetailedProceduresHandler</c> / <c>ExportDetailedProceduresHandler</c>
/// (recorte de blancos, categoría en minúsculas), sin tocar esas clases (AC7).
/// </summary>
public static class NetworkDetailedReportScope
{
    /// <summary>Filtro resuelto para el repositorio de red, o el código de error.</summary>
    public static (NetworkDetailedReportFilter? Filter, string? Error) Resolve(TenantScope? scope, NetworkDetailedReportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (tenantIds, error) = NetworkAnalyticsScope.Resolve(scope, request.ChildTenantId, request.From, request.To);
        if (error is not null)
            return (null, error);

        return (new NetworkDetailedReportFilter(
            tenantIds!,
            request.From,
            request.To,
            request.TransitOfficeId,
            request.ProcedureTypeId,
            Normalize(request.Category)?.ToLowerInvariant(),
            Normalize(request.Status),
            Normalize(request.ReferenceNumber),
            Normalize(request.PersonDocument),
            Normalize(request.PersonName),
            request.HasTransformation,
            request.IsLeasing), null);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Consulta paginada del reporte de red (AC1/AC2).</summary>
public sealed record GetNetworkDetailedProceduresQuery(
    TenantScope? Scope,
    NetworkDetailedReportRequest Request,
    int Page,
    int PageSize);

/// <summary>
/// Listado paginado del reporte de red: misma paginación (por defecto y tope) que
/// <see cref="GetDetailedProceduresHandler"/>; el repositorio recibe el filtro resuelto.
/// </summary>
public sealed class GetNetworkDetailedProceduresHandler(INetworkDetailedReportReadRepository repo)
{
    public async Task<(NetworkAnalyticsOutcome<NetworkDetailedProceduresPageDto>? Result, string? Error)> HandleAsync(
        GetNetworkDetailedProceduresQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (filter, error) = NetworkDetailedReportScope.Resolve(query.Scope, query.Request);
        if (error is not null)
            return (null, error);

        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0
            ? GetDetailedProceduresHandler.DefaultPageSize
            : Math.Min(query.PageSize, GetDetailedProceduresHandler.MaxPageSize);

        var result = await repo.GetNetworkProceduresAsync(filter!, page, pageSize, ct).ConfigureAwait(false);
        return (new NetworkAnalyticsOutcome<NetworkDetailedProceduresPageDto>(result.Items, result.ReachedTenantIds), null);
    }
}

/// <summary>
/// Validación de la exportación del reporte de red (AC5): devuelve el MISMO filtro resuelto que el
/// listado para la misma petición; el archivo sale de la misma consulta.
/// </summary>
public static class ExportNetworkDetailedProceduresHandler
{
    public static (NetworkDetailedReportFilter? Filter, string? Error) Validate(TenantScope? scope, NetworkDetailedReportRequest request) =>
        NetworkDetailedReportScope.Resolve(scope, request);
}
