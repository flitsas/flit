using System.Security.Claims;
using Flit.Analytics.Application.IctQueries;
using Flit.Queries.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Analytics;

/// <summary>
/// Consultas propias de la empresa sobre sus pre-trámites de Integración con Terceros (ICT): el
/// usuario arma su búsqueda sobre el pipeline de validación, la guarda y la exporta. El gemelo de
/// <c>/analytics/queries</c>, un paso antes del trámite.
///
/// <para>Ruta bajo <c>/api/v1/analytics/*</c> y NO bajo <c>/api/v1/ict/*</c> a propósito: ese
/// segundo prefijo lo enruta el Gateway hacia el microservicio core-ict, no hacia core-api — usarlo
/// aquí rompería el ruteo.</para>
///
/// <para>La ejecución va por <c>POST</c> aunque sea una lectura, mismo motivo que en
/// <c>CompanyQueriesEndpoints</c>: la definición puede llevar listas de placas pegadas desde Excel.</para>
///
/// <para><b>Dónde está cada control.</b> El aislamiento —que una empresa jamás vea pre-trámites de
/// otra— se resuelve aquí con el mismo <c>TryResolveTenant</c> que el resto de analítica.</para>
/// </summary>
public static class IctQueriesEndpoints
{
    public static IEndpointRouteBuilder MapIctQueriesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/analytics/ict-queries")
            .RequireAuthorization()
            .WithTags("Analytics · Consultas de ICT");

        group.MapGet("/fields", GetFieldsAsync)
            .WithName("IctQueryFields")
            .WithSummary("Campos por los que se puede consultar sobre los pre-trámites de ICT")
            .WithDescription("El constructor de filtros se pinta a partir de esta respuesta. Las "
                + "opciones de 'tipo_tramite', 'secretaria' y 'cliente_integracion' vienen resueltas "
                + "con lo que esta empresa tiene de verdad.")
            .Produces<IReadOnlyList<QueryFieldDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/run", RunAsync)
            .WithName("IctQueryRun")
            .WithSummary("Ejecuta una consulta y devuelve una página de pre-trámites de ICT")
            .WithDescription("Una fila es un pre-trámite. Cuando la consulta lista placas, VIN o "
                + "radicados concretos, 'cobertura' dice qué pasó con cada valor pedido.")
            .Produces<IctQueryResultDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/saved", ListSavedAsync)
            .WithName("IctQuerySavedList")
            .WithSummary("Consultas guardadas del usuario, más las de fábrica")
            .WithDescription("Las de fábrica van al final y con 'deFabrica' en true: no se "
                + "persisten, no se pueden editar y guardarlas las duplica.")
            .Produces<IReadOnlyList<SavedQueryDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/saved", SaveAsync)
            .WithName("IctQuerySave")
            .WithSummary("Guarda una consulta nueva o actualiza una propia")
            .Produces<SavedQueryDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict);

        group.MapDelete("/saved/{id:guid}", DeleteSavedAsync)
            .WithName("IctQueryDelete")
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
        [FromServices] ExecuteIctQueryHandler handler,
        [FromBody] RunRequest body,
        CancellationToken cancellationToken,
        [FromQuery] Guid? tenantId = null)
    {
        if (!AnalyticsEndpointsHelpers.TryResolveTenant(httpContext.User, tenantId, out var tenant, out var error))
        {
            return error!;
        }

        var request = ExecuteIctQueryHandler.BuildRequest(body?.Definition, body?.Page, body?.PageSize);
        var result = await handler.HandleAsync(tenant, request, cancellationToken).ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static async Task<IResult> GetFieldsAsync(
        HttpContext httpContext,
        [FromServices] GetIctQueryFieldsHandler handler,
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
        [FromServices] ListIctSavedQueriesHandler handler,
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
        [FromServices] SaveIctQueryHandler handler,
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

        var input = SaveIctQueryHandler.BuildInput(body?.Nombre, body?.Descripcion, body?.Definition);

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
        [FromServices] DeleteIctSavedQueryHandler handler,
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

    private static IResult NoUser() =>
        Results.Json(
            new { error = "Token inválido: falta claim sub" },
            statusCode: StatusCodes.Status401Unauthorized);
}
