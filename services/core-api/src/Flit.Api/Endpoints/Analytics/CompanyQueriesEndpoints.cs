using System.Security.Claims;
using Flit.Analytics.Application.CompanyQueries;
using Flit.Api.Authorization;
using Flit.Queries.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Analytics;

/// <summary>
/// Consultas propias de la empresa gestora: el usuario arma su búsqueda sobre los trámites que
/// gestiona, la guarda y la exporta. El gemelo de <c>/admin/ot/queries</c>, del otro lado del
/// trámite.
///
/// <para>La ejecución va por <c>POST</c> aunque sea una lectura. No es un descuido: la definición
/// lleva listas de placas pegadas desde Excel, y meterlas en la barra de direcciones las expondría
/// en los registros de los proxies y chocaría con el límite de longitud de URL a las pocas decenas.</para>
///
/// <para><b>Dónde está cada control.</b> El aislamiento —que una empresa jamás vea trámites de
/// otra— se resuelve aquí, con el mismo <c>TryResolveTenant</c> que el resto de analítica: es la
/// frontera de seguridad y no puede depender del cliente. La VISIBILIDAD de la pestaña es RBAC de
/// pantalla, como en las otras cinco del módulo de reportes. Son dos cosas distintas a propósito:
/// esconder una pestaña no protege un dato, y proteger un dato no debería obligar a esconder la
/// pestaña.</para>
/// </summary>
public static class CompanyQueriesEndpoints
{
    public static IEndpointRouteBuilder MapCompanyQueriesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/analytics/queries")
            .RequireAuthorization()
            .WithTags("Analytics · Consultas de la empresa");

        group.MapGet("/fields", GetFieldsAsync)
            .WithName("CompanyQueryFields")
            .WithSummary("Campos por los que se puede consultar")
            .WithDescription("El constructor de filtros se pinta a partir de esta respuesta, así que "
                + "un campo nuevo aparece en pantalla sin tocar el frontend. Las opciones de "
                + "'organismo', 'tipo_tramite', 'radicado_por' y 'metodo_pago' vienen resueltas con "
                + "lo que esta empresa tiene de verdad.")
            .Produces<IReadOnlyList<QueryFieldDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/run", RunAsync)
            .WithName("CompanyQueryRun")
            .WithSummary("Ejecuta una consulta y devuelve una página de trámites")
            .WithDescription("Una fila es un trámite: las condiciones sobre tablas hijas se resuelven "
                + "como 'existe' y nunca como cruce. Cuando la consulta lista placas, VIN o "
                + "radicados concretos, 'cobertura' dice qué pasó con cada valor pedido — si salió, "
                + "si no existe en la empresa, o qué filtro lo dejó fuera.")
            .Produces<CompanyQueryResultDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/saved", ListSavedAsync)
            .WithName("CompanyQuerySavedList")
            .WithSummary("Consultas guardadas del usuario, más las de fábrica")
            .WithDescription("Las de fábrica van al final y con 'deFabrica' en true: no se persisten, "
                + "no se pueden editar y guardarlas las duplica.")
            .Produces<IReadOnlyList<SavedQueryDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/saved", SaveAsync)
            .WithName("CompanyQuerySave")
            .WithSummary("Guarda una consulta nueva o actualiza una propia")
            .Produces<SavedQueryDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict);

        group.MapDelete("/saved/{id:guid}", DeleteSavedAsync)
            .WithName("CompanyQueryDelete")
            .WithSummary("Borra una consulta guardada del usuario")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>Cuerpo de la ejecución: la definición más la página que se quiere ver.</summary>
    public sealed record RunRequest(QueryDefinition? Definition, int? Page, int? PageSize);

    /// <summary>Cuerpo del guardado. <c>Id</c> nulo crea; con id actualiza la del propio usuario.</summary>
    public sealed record SaveRequest(
        Guid? Id,
        string? Nombre,
        string? Descripcion,
        QueryDefinition? Definition);

    private static async Task<IResult> RunAsync(
        HttpContext httpContext,
        [FromServices] ExecuteCompanyQueryHandler handler,
        [FromBody] RunRequest body,
        CancellationToken cancellationToken,
        [FromQuery] Guid? tenantId = null)
    {
        if (!AnalyticsEndpointsHelpers.TryResolveTenant(httpContext.User, tenantId, out var tenant, out var error))
        {
            return error!;
        }

        var request = ExecuteCompanyQueryHandler.BuildRequest(body?.Definition, body?.Page, body?.PageSize);
        var result = await handler.HandleAsync(tenant, request, cancellationToken).ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static async Task<IResult> GetFieldsAsync(
        HttpContext httpContext,
        [FromServices] GetCompanyQueryFieldsHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] Guid? tenantId = null)
    {
        if (!AnalyticsEndpointsHelpers.TryResolveTenant(httpContext.User, tenantId, out var tenant, out var error))
        {
            return error!;
        }

        var result = await handler.HandleAsync(tenant, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> ListSavedAsync(
        HttpContext httpContext,
        [FromServices] ListCompanySavedQueriesHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] Guid? tenantId = null)
    {
        if (!AnalyticsEndpointsHelpers.TryResolveTenant(httpContext.User, tenantId, out var tenant, out var error))
        {
            return error!;
        }

        if (ResolveUser(httpContext.User) is not Guid userId)
        {
            return NoUser();
        }

        var result = await handler.HandleAsync(tenant, userId, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> SaveAsync(
        HttpContext httpContext,
        [FromServices] SaveCompanyQueryHandler handler,
        [FromBody] SaveRequest body,
        CancellationToken cancellationToken,
        [FromQuery] Guid? tenantId = null)
    {
        if (!AnalyticsEndpointsHelpers.TryResolveTenant(httpContext.User, tenantId, out var tenant, out var error))
        {
            return error!;
        }

        if (ResolveUser(httpContext.User) is not Guid userId)
        {
            return NoUser();
        }

        var input = SaveCompanyQueryHandler.BuildInput(body?.Nombre, body?.Descripcion, body?.Definition);

        try
        {
            var result = await handler
                .HandleAsync(tenant, userId, body?.Id, input, cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(result);
        }
        catch (SavedQueryNameTakenException ex)
        {
            return Results.Conflict(new { error = ex.Message, code = "NOMBRE_REPETIDO" });
        }
        catch (SavedQueryLimitException ex)
        {
            return Results.Conflict(new { error = ex.Message, code = "LIMITE_CONSULTAS" });
        }
    }

    private static async Task<IResult> DeleteSavedAsync(
        HttpContext httpContext,
        [FromServices] DeleteCompanySavedQueryHandler handler,
        Guid id,
        CancellationToken cancellationToken,
        [FromQuery] Guid? tenantId = null)
    {
        if (!AnalyticsEndpointsHelpers.TryResolveTenant(httpContext.User, tenantId, out var tenant, out var error))
        {
            return error!;
        }

        if (ResolveUser(httpContext.User) is not Guid userId)
        {
            return NoUser();
        }

        var deleted = await handler.HandleAsync(tenant, userId, id, cancellationToken).ConfigureAwait(false);

        return deleted
            ? Results.NoContent()
            : Results.NotFound(new { error = "La consulta no existe o no es suya." });
    }

    private static Guid? ResolveUser(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue("sub"), out var sub) ? sub : null;

    /// <summary>
    /// Las consultas guardadas son de una persona, así que sin identificarla no hay nada que listar
    /// ni dónde guardar. Consultar sí se puede: eso solo necesita la empresa.
    /// </summary>
    private static IResult NoUser() =>
        Results.Json(
            new { error = "Token inválido: falta claim sub" },
            statusCode: StatusCodes.Status401Unauthorized);
}
