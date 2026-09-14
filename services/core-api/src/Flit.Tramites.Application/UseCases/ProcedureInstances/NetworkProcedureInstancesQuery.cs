using Flit.Queries.Domain;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #12358 (Feature #12257, épica #12235) — política de las rutas consolidadas de la red
/// (<c>/api/v1/tramites/network/**</c>): solo una cabeza de grupo con hijos tiene «red». Un SuperAdmin
/// (<see cref="TenantScope.IsAll"/>) tiene el listado global por sus rutas de siempre; un cliente sin
/// jerarquía (<c>Single</c>) no tiene nada que consolidar. Ninguno de los dos entra por aquí.
/// <para>
/// Códigos de error (contrato con la API): <see cref="ScopeRequired"/> ⇒ 403;
/// <see cref="ChildOutOfScope"/> ⇒ 403 SIN ejecutar la consulta (el hijo pedido no está en el
/// conjunto de lectura; jamás se «amplía» el alcance para complacer al filtro).
/// </para>
/// </summary>
public static class NetworkScopePolicy
{
    public const string ScopeRequired = "network_scope_required";
    public const string ChildOutOfScope = "network_child_out_of_scope";

    /// <summary><c>null</c> si <paramref name="scope"/> es una cabeza de grupo; si no, el código de error.</summary>
    public static string? Validate(TenantScope? scope) =>
        scope is null || scope.IsAll || !scope.IsGroup ? ScopeRequired : null;

    /// <summary>
    /// Acota el alcance al cliente <paramref name="childTenantId"/> (que puede ser la propia cabeza).
    /// Sin filtro devuelve el alcance intacto. Con un cliente fuera de <see cref="TenantScope.ReadTenantIds"/>
    /// devuelve <see cref="ChildOutOfScope"/>: la decisión se toma en memoria, sin tocar la base.
    /// </summary>
    public static (TenantScope? Scope, string? Error) Narrow(TenantScope scope, Guid? childTenantId)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (childTenantId is not { } child || child == Guid.Empty)
            return (scope, null);

        return scope.ReadTenantIds.Contains(child)
            ? (TenantScope.Single(child), null)
            : (null, ChildOutOfScope);
    }
}

/// <summary>
/// Listado consolidado de la red (HU #12358, AC1/AC2/AC3/AC10). Mismos filtros, orden, enriquecimiento
/// y DTO de fila (<see cref="InstanceSummaryDto"/>, con <c>TenantId</c> y <c>CompaniaNombre</c> del
/// cliente dueño) que <see cref="ListProcedureInstancesFilteredHandler"/>; lo único distinto es el
/// alcance: <see cref="TenantScope"/> en vez de <c>Guid?</c>. <see cref="ProcedureInstanceListRequest.TenantId"/>
/// se IGNORA a propósito (el alcance nunca viene del caller). Página siempre acotada por
/// <see cref="ListProcedureInstancesHandler.MaxItems"/>.
/// </summary>
public sealed class NetworkListProcedureInstancesHandler(IProcedureInstanceRepository repo)
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

        var (instances, total) = await repo.ListWithSummaryGraphFilteredAsync(
            effective!, Math.Max(0, request.Skip), take, filter, sortBy, direction, ct);

        var items = await ListProcedureInstancesFilteredHandler.ToSummariesAsync(repo, instances, ct);
        return (items, total, null);
    }
}

/// <summary>
/// Conteo por estado del universo consolidado de la red (HU #12358). Misma semántica que
/// <see cref="CountProcedureInstancesByStatusHandler"/> (ignora <c>Estados</c>; devuelve las siete
/// claves más <c>subsanacion</c>), con el alcance por <see cref="TenantScope"/>.
/// </summary>
public sealed class NetworkCountProcedureInstancesByStatusHandler(IProcedureInstanceRepository repo)
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
/// hijo por <c>GET /instances/{id}</c> (mismo mapeo <c>GetProcedureInstanceHandler.ToDetail</c>).
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

        var events = await GetProcedureInstanceHandler.BuildEventsAsync(repo, instance.Events, ct).ConfigureAwait(false);
        var names = await repo.GetTenantNamesAsync([instance.TenantId], ct);

        return (new NetworkProcedureInstanceDetailDto(
            instance.TenantId,
            names.GetValueOrDefault(instance.TenantId),
            GetProcedureInstanceHandler.ToDetail(instance, events)), null);
    }
}
