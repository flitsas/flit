using Flit.Admin.Application.Companies.Deeds.ListActiveDeeds;
using Flit.Admin.Application.Companies.LegalRepresentatives.FindByNit;
using Flit.Api.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// Endpoints de CONSUMO del wizard de trámites para el directorio de representantes/escrituras
/// (HU #10903, ADR-0033 §5.4). A diferencia de la gestión admin (SuperAdmin), los usa el OPERADOR de
/// la compañía sobre SU tenant: el tenant lo impone el <see cref="TenantEnforcementMiddleware"/> desde
/// el JWT (las rutas <c>/api/v1/tramites/deeds</c> y <c>/api/v1/tramites/legal-representatives</c>
/// están registradas como runtime-scoped), de modo que un company-user no lea el directorio de otra
/// compañía cambiando <c>X-Tenant-Id</c>. Un SuperAdmin debe acotar la compañía con <c>X-Tenant-Id</c>.
/// </summary>
public static class LegalRepresentativeConsumptionEndpoints
{
    public static IEndpointRouteBuilder MapLegalRepresentativeConsumptionEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/tramites")
            .RequireAuthorization()
            .WithTags("Trámites · Consumo directorio representantes");

        // GET /deeds/active — escrituras activas y vigentes del tenant (collapse del primer paso del
        // wizard). Devuelve [{ nit, name, diasRestantes, vigenciaHasta }].
        group.MapGet("/deeds/active", ListActiveDeedsAsync)
            .WithName("TramitesActiveDeeds")
            .WithSummary("Escrituras activas y vigentes de la compañía autenticada")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // GET /legal-representatives/lookup?nit=NNN — precarga comprador/vendedor por NIT. 200 con el
        // match (compañía + representante + banderas de firma/identidad vigentes) o 404 si no hay match.
        group.MapGet("/legal-representatives/lookup", LookupByNitAsync)
            .WithName("TramitesLegalRepresentativeLookup")
            .WithSummary("Precarga de representante legal del tenant por NIT")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListActiveDeedsAsync(
        HttpContext http,
        ListActiveDeedsForTenantHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenant(http, out var tenantId, out var problem))
        {
            return problem;
        }

        var items = await handler
            .HandleAsync(new ListActiveDeedsForTenantQuery { TenantId = tenantId }, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new { items });
    }

    private static async Task<IResult> LookupByNitAsync(
        HttpContext http,
        string? nit,
        FindRepresentativeByNitHandler handler,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(nit))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
                detail: "Indique el NIT a consultar (query param 'nit').");
        }

        if (!TryResolveTenant(http, out var tenantId, out var problem))
        {
            return problem;
        }

        var result = await handler
            .HandleAsync(new FindRepresentativeByNitQuery { TenantId = tenantId, Nit = nit }, cancellationToken)
            .ConfigureAwait(false);

        // Sin match: 404 (el FE cae a la consulta RUES/RUNT normal). No se filtra el NIT en el detalle.
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    /// <summary>
    /// Tenant resuelto por el <see cref="TenantEnforcementMiddleware"/> desde el JWT. Company-user →
    /// su tenant; SuperAdmin → debe acotar con <c>X-Tenant-Id</c> (si no, 400). <c>null</c> solo
    /// ocurre para un SuperAdmin sin acotar.
    /// </summary>
    private static bool TryResolveTenant(HttpContext http, out Guid tenantId, out IResult problem)
    {
        tenantId = Guid.Empty;
        problem = Results.Empty;

        if (http.Items.TryGetValue(TenantEnforcementMiddleware.TenantItemKey, out var t) && t is Guid g)
        {
            tenantId = g;
            return true;
        }

        problem = Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
            detail: "Indique la compañía a consultar (header X-Tenant-Id).");
        return false;
    }
}
