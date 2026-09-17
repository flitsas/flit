using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Api.Authorization;

/// <summary>
/// HU #12358 (Feature #12257, épica #12235) — guard ÚNICO de escritura sobre trámites para las cabezas
/// de grupo (AC4): la lectura consolidada de la red NO otorga escritura sobre los hijos. Toda escritura
/// se decide con <see cref="TenantScope.CanWrite"/> — nunca con <see cref="TenantScope.CanRead"/>.
/// <list type="bullet">
///   <item>Rutas bajo <c>/api/v1/tramites/**</c>: el alcance ya está en <c>HttpContext.Items</c>
///   (<see cref="RequestTenantResolver.ScopeFromItems"/>, puesto por <c>TenantEnforcementMiddleware</c>).</item>
///   <item>Rutas <c>/api/v1/admin/tramites/*</c> (fuera del middleware): se resuelve por petición vía
///   <see cref="ITenantScopeResolver"/> con el tenant del JWT; SuperAdmin queda fuera (alcance total).</item>
///   <item>Cliente sin jerarquía (<c>Single</c>): el guard NO actúa y el comportamiento es idéntico a
///   hoy (el tenant ya viene impuesto desde el token; un id ajeno sigue resolviendo 404 en el handler).</item>
///   <item>Cabeza de grupo (<c>Group</c>): si el trámite existe y <c>!CanWrite(dueño)</c> ⇒ 403
///   <c>{ error: "network_write_forbidden" }</c> antes de llegar al handler.</item>
/// </list>
/// El dueño del trámite se resuelve con <see cref="IProcedureInstanceOwnerLookup"/> (solo el id del
/// tenant, sin datos). Un id inexistente pasa al handler, que responde 404 como siempre.
/// </summary>
public static class TenantWriteGuard
{
    public const string ErrorCode = "network_write_forbidden";

    private static readonly IResult Forbidden = Results.Json(new { error = ErrorCode }, statusCode: StatusCodes.Status403Forbidden);

    /// <summary>
    /// Comprobación síncrona cuando el caller ya conoce el tenant dueño: 403 si hay alcance de grupo
    /// en la petición y no puede escribir sobre <paramref name="tenantIdDelTramite"/>; <c>null</c> = sigue.
    /// </summary>
    public static IResult? RequireCanWrite(HttpContext http, Guid tenantIdDelTramite)
    {
        ArgumentNullException.ThrowIfNull(http);
        var scope = RequestTenantResolver.ScopeFromItems(http);
        return Decide(scope, tenantIdDelTramite);
    }

    /// <summary>
    /// Resuelve el dueño de <paramref name="procedureInstanceId"/> y decide. <c>null</c> = sigue
    /// (sin alcance de grupo, id inexistente, o la cabeza escribe sobre sí misma).
    /// </summary>
    public static async Task<IResult?> RejectIfCannotWriteAsync(HttpContext http, Guid procedureInstanceId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        var scope = await ResolveScopeAsync(http, ct).ConfigureAwait(false);
        if (!Applies(scope))
            return null;

        var lookup = http.RequestServices.GetService<IProcedureInstanceOwnerLookup>();
        if (lookup is null)
            return null;

        var owner = await lookup.GetOwnerTenantIdAsync(procedureInstanceId, ct).ConfigureAwait(false);
        return owner is { } tenantId ? Decide(scope, tenantId) : null;
    }

    /// <summary>
    /// Variante para escrituras en lote (p. ej. <c>POST /instances/pause-massive</c>): basta UN trámite
    /// ajeno en <paramref name="procedureInstanceIds"/> para rechazar toda la petición.
    /// </summary>
    public static async Task<IResult?> RejectIfAnyCannotWriteAsync(
        HttpContext http, IReadOnlyCollection<Guid> procedureInstanceIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(procedureInstanceIds);
        var scope = await ResolveScopeAsync(http, ct).ConfigureAwait(false);
        if (!Applies(scope) || procedureInstanceIds.Count == 0)
            return null;

        var lookup = http.RequestServices.GetService<IProcedureInstanceOwnerLookup>();
        if (lookup is null)
            return null;

        var owners = await lookup.GetOwnerTenantIdsAsync(procedureInstanceIds, ct).ConfigureAwait(false);
        foreach (var owner in owners.Values)
        {
            if (Decide(scope, owner) is { } forbidden)
                return forbidden;
        }

        return null;
    }

    /// <summary>El guard solo actúa para una cabeza de grupo; <c>All</c> y <c>Single</c> se comportan como hoy.</summary>
    private static bool Applies(TenantScope? scope) => scope is { IsAll: false, IsGroup: true };

    private static IResult? Decide(TenantScope? scope, Guid tenantIdDelTramite) =>
        Applies(scope) && !scope!.CanWrite(tenantIdDelTramite) ? Forbidden : null;

    /// <summary>
    /// Alcance de la petición: el que dejó el middleware (rutas runtime) o, si la ruta no está cubierta
    /// (admin), el resuelto desde la BD con el tenant del JWT. SuperAdmin ⇒ <c>null</c> (sin restricción,
    /// igual que <c>All</c>). Cerrado por defecto: sin tenant o sin resolver ⇒ <c>null</c> (el guard no
    /// actúa y el handler conserva su comportamiento actual).
    /// </summary>
    private static async Task<TenantScope?> ResolveScopeAsync(HttpContext http, CancellationToken ct)
    {
        if (RequestTenantResolver.ScopeFromItems(http) is { } fromItems)
            return fromItems;

        var user = http.User;
        if (user?.Identity?.IsAuthenticated != true || RequestTenantResolver.IsSuperAdmin(user))
            return null;

        if (!RequestTenantResolver.TryResolveNonEmptyTenantId(user, out var tenantId))
            return null;

        var resolver = http.RequestServices.GetService<ITenantScopeResolver>();
        if (resolver is null)
            return null;

        try
        {
            var scope = await resolver.ResolveAsync(tenantId, ct).ConfigureAwait(false);
            return scope is null || scope.IsAll ? null : scope;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Fail-closed para la LECTURA es Single; para el guard de ESCRITURA «sin alcance» significa
            // que no se otorga nada nuevo: el handler sigue con el tenant impuesto del token.
            return null;
        }
    }
}
