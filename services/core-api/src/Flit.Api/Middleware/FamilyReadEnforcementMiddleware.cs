using Flit.Api.Authorization;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Middleware;

/// <summary>
/// HU #13664 (Feature #13636 F-F, Épica #12753) — «Ver por familia» en UN solo punto: toda ruta que identifica un trámite
/// por id (<see cref="Covers"/>) responde 403 si el usuario no tiene el «.read» de la familia del trámite
/// (<see cref="IProcedureFamilyPermissionGate.CanRead"/>), sin tocar cada endpoint. Patrón de
/// <see cref="TenantWriteGuardMiddleware"/>: va DESPUÉS de <see cref="TenantEnforcementMiddleware"/> (usa el alcance de los
/// Items para acotar la consulta) y del enrutamiento (usa <c>RouteValues</c>).
/// <list type="bullet">
///   <item>Cubre TODOS los verbos: leer, y también las rutas que avanzan el estado (submit, transition, finalize-draft…) —
///   para ellas basta «Ver»; la creación ya exige «Crear» en su ruta.</item>
///   <item>Flag apagado, sin usuario o gRPC (<see cref="IProcedureFamilyPermissionGate.IsActive"/> = false): pasa sin consultar.</item>
///   <item>Un id que no existe en el alcance del usuario pasa al handler (404 como siempre); la consulta es liviana
///   (<see cref="IProcedureInstanceFamilyLookup"/>) y una sola por petición.</item>
/// </list>
/// Uso de ejemplo: <c>GET /api/v1/tramites/instances/{id}/status-history</c> de un traspaso sin <c>traspasos.read</c> → 403.
/// </summary>
public sealed class FamilyReadEnforcementMiddleware(RequestDelegate next)
{
    /// <summary>Plantillas (inicio del patrón de ruta) con el nombre del parámetro que lleva el id del trámite.</summary>
    public static readonly IReadOnlyList<(string PatternPrefix, string IdKey)> CoveredRoutes =
    [
        ("/api/v1/tramites/instances/{id", "id"),
        // HU #12358 — detalle consolidado y documentos de un trámite de la red.
        ("/api/v1/tramites/network/instances/{id", "id"),
        // Requisitos documentales del trámite (TramitesEndpoints).
        ("/api/v1/tramites/{tramiteId", "tramiteId"),
    ];

    /// <summary>
    /// Rutas de la familia de prefijos cubiertos que NO se guardan, con motivo. Hoy ninguna: toda ruta por id de trámite
    /// se guarda. Se agrega aquí (patrón exacto) solo con justificación; un test falla si una ruta queda sin cubrir.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Exempt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>La ruta lleva el id de un trámite y está guardada por este middleware.</summary>
    public static bool Covers(RouteEndpoint endpoint) => TryGetIdKey(endpoint, out _);

    /// <summary>La ruta es por id de trámite (<see cref="CoveredRoutes"/>), exenta o no.</summary>
    public static bool IsProcedureByIdRoute(RouteEndpoint endpoint) => MatchKey(endpoint.RoutePattern.RawText) is not null;

    private static string? MatchKey(string? raw)
    {
        if (raw is null)
            return null;
        foreach (var (prefix, key) in CoveredRoutes)
        {
            if (raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return key;
        }

        return null;
    }

    private static bool TryGetIdKey(RouteEndpoint endpoint, out string key)
    {
        var raw = endpoint.RoutePattern.RawText;
        key = MatchKey(raw) ?? string.Empty;
        return key.Length > 0 && !Exempt.ContainsKey(raw!);
    }

    public async Task InvokeAsync(
        HttpContext context, IProcedureFamilyPermissionGate gate, IProcedureInstanceFamilyLookup lookup)
    {
        if (gate.IsActive
            && context.GetEndpoint() is RouteEndpoint endpoint
            && TryGetIdKey(endpoint, out var key)
            && context.Request.RouteValues.TryGetValue(key, out var raw)
            && raw is string text
            && Guid.TryParse(text, out var id)
            && id != Guid.Empty)
        {
            var scope = RequestTenantResolver.ScopeFromItems(context);
            IReadOnlyCollection<Guid>? tenantIds = scope is null || scope.IsAll ? null : scope.ReadTenantIds;
            var family = await lookup.GetFamilyAsync(id, tenantIds, context.RequestAborted).ConfigureAwait(false);
            if (family is { } f && !gate.CanRead(f))
            {
                await Denied().ExecuteAsync(context).ConfigureAwait(false);
                return;
            }
        }

        await next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Variante para rutas con los ids en el body (<c>POST /instances/pause-massive</c>): basta UN trámite de una familia sin
    /// «.read» para rechazar toda la petición. <c>null</c> = sigue (exigencia apagada, o todos permitidos).
    /// </summary>
    public static async Task<IResult?> RejectIfAnyUnreadableAsync(HttpContext http, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(ids);
        var gate = http.RequestServices.GetService<IProcedureFamilyPermissionGate>();
        var lookup = http.RequestServices.GetService<IProcedureInstanceFamilyLookup>();
        if (gate is not { IsActive: true } || lookup is null || ids.Count == 0)
            return null;

        var scope = RequestTenantResolver.ScopeFromItems(http);
        IReadOnlyCollection<Guid>? tenantIds = scope is null || scope.IsAll ? null : scope.ReadTenantIds;
        var families = await lookup.GetFamiliesAsync(ids, tenantIds, ct).ConfigureAwait(false);
        return families.Values.Any(f => !gate.CanRead(f)) ? Denied() : null;
    }

    /// <summary>Mismo cuerpo que el 403 del detalle (<c>GET /instances/{id}</c>).</summary>
    internal static IResult Denied() => Results.Problem(
        statusCode: StatusCodes.Status403Forbidden,
        title: "Forbidden",
        detail: "No tienes permiso para ver trámites de esta familia.",
        extensions: new Dictionary<string, object?> { ["code"] = "family_permission_denied" });
}
