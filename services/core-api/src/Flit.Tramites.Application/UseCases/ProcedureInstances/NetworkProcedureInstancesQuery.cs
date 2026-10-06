using Flit.Queries.Domain;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Listado consolidado de la red (HU #12358, AC1/AC2/AC3/AC10). Mismos filtros, orden, enriquecimiento
/// y DTO de fila (<see cref="InstanceSummaryDto"/>, con <c>TenantId</c> y <c>CompaniaNombre</c> del
/// cliente dueño) que <see cref="ListProcedureInstancesFilteredHandler"/>; lo único distinto es el
/// alcance: <see cref="TenantScope"/> en vez de <c>Guid?</c>. <see cref="ProcedureInstanceListRequest.TenantId"/>
/// se IGNORA a propósito (el alcance nunca viene del caller). Página siempre acotada por
/// <see cref="ListProcedureInstancesHandler.MaxItems"/>.
/// </summary>
public sealed class NetworkListProcedureInstancesHandler(
    IProcedureInstanceRepository repo, BusquedaRapidaResolver? busquedaRapida = null)
{
    public async Task<(IReadOnlyList<InstanceSummaryDto> Items, int Total, string? Error)> HandleAsync(
        TenantScope? scope,
        Guid? childTenantId,
        ProcedureInstanceListRequest request,
        CancellationToken ct = default)
    {
        if (NetworkScopePolicy.Validate(scope) is { } scopeError)
            return ([], 0, scopeError);

        var (effective, narrowError) = NetworkScopePolicy.Narrow(scope!, childTenantId);
        if (narrowError is not null)
            return ([], 0, narrowError);

        var sortBy = ProcedureInstanceSortFields.Resolve(request.SortBy);
        var direction = request.SortDescending ? SortDirection.Descending : SortDirection.Ascending;
        var filter = ListProcedureInstancesFilteredHandler.BuildFilter(request);
        var take = ListProcedureInstancesFilteredHandler.ClampTake(request.Take);
        // Epic #12686 — el atajo de la búsqueda rápida se evalúa sobre el alcance de la red.
        filter = await AplicarBusquedaRapidaEnRedAsync(repo, busquedaRapida, effective!, request, filter, ct);

        var (instances, total) = await repo.ListWithSummaryGraphFilteredAsync(
            effective!, Math.Max(0, request.Skip), take, filter, sortBy, direction, ct);

        var items = await ListProcedureInstancesFilteredHandler.ToSummariesAsync(repo, instances, ct);
        return (items, total, null);
    }

    internal static async Task<ProcedureInstanceListFilter> AplicarBusquedaRapidaEnRedAsync(
        IProcedureInstanceRepository repo,
        BusquedaRapidaResolver? resolver,
        TenantScope alcance,
        ProcedureInstanceListRequest request,
        ProcedureInstanceListFilter filter,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.BusquedaRapida))
            return filter;
        resolver ??= new BusquedaRapidaResolver(repo);
        return await resolver.AplicarAsync(
            filter,
            request.BusquedaRapida,
            (f, take, token) => repo.ListWithSummaryGraphFilteredAsync(
                alcance, 0, take, f, ProcedureInstanceSortBy.Default, SortDirection.Descending, token),
            request.UsuarioActualId,
            ct).ConfigureAwait(false);
    }
}

/// <summary>
/// Conteo por estado del universo consolidado de la red (HU #12358). Misma semántica que
/// <see cref="CountProcedureInstancesByStatusHandler"/> (ignora <c>Estados</c>; devuelve las siete
/// claves más <c>subsanacion</c>), con el alcance por <see cref="TenantScope"/>.
/// </summary>
public sealed class NetworkCountProcedureInstancesByStatusHandler(
    IProcedureInstanceRepository repo, BusquedaRapidaResolver? busquedaRapida = null)
{
    public async Task<(IReadOnlyDictionary<string, int>? Counts, string? Error)> HandleAsync(
        TenantScope? scope,
        Guid? childTenantId,
        ProcedureInstanceListRequest request,
        CancellationToken ct = default)
    {
        var (result, error) = await HandleWithReachAsync(scope, childTenantId, request, ct);
        return (result?.Counts, error);
    }

    /// <summary>
    /// HU #12361 — igual que <see cref="HandleAsync"/> pero devuelve además los clientes DISTINTOS del
    /// alcance con al menos un trámite bajo el filtro (<see cref="NetworkStatusCountsResult.ReachedTenantIds"/>):
    /// los conteos por estado no dicen a qué hijos alcanzan y la auditoría de acceso consolidado lo
    /// necesita para registrar (o no, AC5) la petición.
    /// </summary>
    public async Task<(NetworkStatusCountsResult? Result, string? Error)> HandleWithReachAsync(
        TenantScope? scope,
        Guid? childTenantId,
        ProcedureInstanceListRequest request,
        CancellationToken ct = default)
    {
        if (NetworkScopePolicy.Validate(scope) is { } scopeError)
            return (null, scopeError);

        var (effective, narrowError) = NetworkScopePolicy.Narrow(scope!, childTenantId);
        if (narrowError is not null)
            return (null, narrowError);

        var filter = ListProcedureInstancesFilteredHandler.BuildFilter(request) with { Estados = null };
        filter = await NetworkListProcedureInstancesHandler.AplicarBusquedaRapidaEnRedAsync(
            repo, busquedaRapida, effective!, request, filter, ct);
        var conteos = await repo.CountByStatusFilteredAsync(effective!, filter, ct);
        var alcanzados = await repo.ListTenantIdsWithMatchesAsync(effective!, filter, ct);

        var resultado = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var estado in TramiteEstado.Todos)
            resultado[estado] = conteos.GetValueOrDefault(estado);
        resultado[TramiteEstado.Subsanacion] = conteos.GetValueOrDefault(TramiteEstado.Subsanacion);

        return (new NetworkStatusCountsResult(resultado, alcanzados), null);
    }
}

/// <summary>Conteos por estado del universo consolidado + clientes alcanzados por el filtro (HU #12361).</summary>
public sealed record NetworkStatusCountsResult(
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyList<Guid> ReachedTenantIds);

/// <summary>
/// Detalle consolidado en solo lectura (HU #12358, AC1/AC5): el cliente dueño (<c>TenantId</c> +
/// <c>TenantName</c>) y EXACTAMENTE el mismo <see cref="ProcedureInstanceDetailDto"/> que ve el propio
/// hijo por <c>GET /instances/{id}</c> (mismo constructor <c>GetProcedureInstanceHandler.BuildDetailAsync</c>).
/// No incluye contenido de documentos ni direcciones prefirmadas: el detalle nunca las tuvo y la
/// ruta de red no las añade.
/// </summary>
public sealed record NetworkProcedureInstanceDetailDto(
    Guid TenantId,
    string? TenantName,
    ProcedureInstanceDetailDto Instance);

public sealed class NetworkGetProcedureInstanceHandler(IProcedureInstanceRepository repo)
{
    public async Task<(NetworkProcedureInstanceDetailDto? Result, string? Error)> HandleAsync(
        Guid id,
        TenantScope? scope,
        CancellationToken ct = default)
    {
        if (NetworkScopePolicy.Validate(scope) is { } scopeError)
            return (null, scopeError);

        var instance = await repo.GetByIdWithDetailsAsync(id, scope!, ct);
        if (instance is null)
            return (null, "not_found");

        // Mismo constructor del DTO que el detalle propio (AC1 HU #12358: «los mismos campos»): cualquier
        // enriquecimiento del detalle vive en BuildDetailAsync y llega aquí sin duplicar la resolución.
        var detail = await GetProcedureInstanceHandler.BuildDetailAsync(repo, instance, ct).ConfigureAwait(false);
        var names = await repo.GetTenantNamesAsync([instance.TenantId], ct);

        return (new NetworkProcedureInstanceDetailDto(
            instance.TenantId,
            names.GetValueOrDefault(instance.TenantId),
            detail), null);
    }
}
