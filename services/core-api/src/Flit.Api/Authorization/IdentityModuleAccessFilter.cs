using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Api.Authorization;

/// <summary>
/// HU #12711 — acceso a la API del módulo Validación de Identidad. Hasta esta HU solo el menú y la URL
/// <c>?m=validaciones</c> ocultaban el módulo; la API respondía a cualquier usuario autenticado.
/// <list type="bullet">
///   <item>SuperAdmin: pasa siempre (mismo bypass que <see cref="PermissionAuthorizationHandler"/>).</item>
///   <item>Resto: necesita AL MENOS UNO de los permisos de la ruta en el claim <c>permissions</c>.</item>
///   <item>Usuario de un organismo de tránsito: 403 aunque tenga el permiso. El Operador OT trae
///   <c>dashboard.read</c> y <c>tramites.read</c>, que abren el listado plano y la bitácora a los
///   gestores; sin esta negación se colaría por ahí.</item>
/// </list>
/// Corre antes del handler: un rechazo no ejecuta ninguna consulta del módulo.
/// </summary>
public sealed class IdentityModuleAccessFilter(IReadOnlyList<string> anyOfPermissions) : IEndpointFilter
{
    public const string PermissionRequiredCode = "identity_permission_required";

    public const string TransitOfficeDeniedCode = "identity_transit_office_denied";

    public IReadOnlyList<string> AnyOfPermissions { get; } = anyOfPermissions;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var http = context.HttpContext;
        var user = http.User;

        if (user.Identity?.IsAuthenticated != true)
            return Results.Unauthorized();

        if (RequestTenantResolver.IsSuperAdmin(user))
            return await next(context);

        var granted = user.FindAll("permissions")
            .Any(c => AnyOfPermissions.Contains(c.Value, StringComparer.OrdinalIgnoreCase));
        if (!granted)
        {
            return Forbidden(
                PermissionRequiredCode,
                "No tienes permiso para usar Validación de Identidad.");
        }

        if (RequestTenantResolver.TryResolveNonEmptyTenantId(user, out var tenantId))
        {
            var probe = http.RequestServices.GetRequiredService<ITransitOfficeTenantProbe>();
            if (await probe.IsTransitOfficeAsync(tenantId, http.RequestAborted))
            {
                return Forbidden(
                    TransitOfficeDeniedCode,
                    "Validación de Identidad no está disponible para organismos de tránsito.");
            }
        }

        return await next(context);
    }

    private static IResult Forbidden(string code, string detail) =>
        Results.Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: "Forbidden",
            detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}

/// <summary>Permisos y combinaciones de <see cref="IdentityModuleAccessFilter"/> por tipo de ruta.</summary>
public static class IdentityModuleAccess
{
    public const string Read = "validaciones.read";
    public const string Manage = "validaciones.manage";
    public const string DashboardRead = "dashboard.read";
    public const string TramitesRead = "tramites.read";

    /// <summary>
    /// Lecturas propias del módulo (por persona, historial, detalle por id, atascadas). <c>manage</c>
    /// también abre la lectura: el menú muestra el módulo con cualquiera de los dos permisos
    /// (<c>/security/modules</c>) y gestionar sin poder ver dejaría la pantalla rota.
    /// </summary>
    public static RouteHandlerBuilder RequireIdentityModuleRead(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(new IdentityModuleAccessFilter([Read, Manage]));

    /// <summary>Escrituras del módulo: crear, editar y reenviar prevalidaciones, reencolar atascadas.</summary>
    public static RouteHandlerBuilder RequireIdentityModuleManage(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(new IdentityModuleAccessFilter([Manage]));

    /// <summary>
    /// Listado plano <c>GET /biometric-validations</c>: además del módulo lo lee el Dashboard de
    /// cualquier gestor (KPIs de identidad), así que abre también con <c>dashboard.read</c>.
    /// </summary>
    public static RouteHandlerBuilder RequireIdentityModuleOrDashboard(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(new IdentityModuleAccessFilter([Read, Manage, DashboardRead]));

    /// <summary>
    /// Bitácora <c>GET /biometric-validations/{id}/audit</c>: además del módulo la consumen el asistente
    /// de trámites y el detalle del trámite (seguimiento de la identidad de una parte), así que abre
    /// también con <c>tramites.read</c>.
    /// </summary>
    public static RouteHandlerBuilder RequireIdentityModuleOrTramites(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(new IdentityModuleAccessFilter([Read, Manage, TramitesRead]));
}
