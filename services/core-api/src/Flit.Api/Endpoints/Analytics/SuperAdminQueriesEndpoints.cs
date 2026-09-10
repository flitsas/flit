using System.Security.Claims;
using Flit.Analytics.Application.CompanyQueries;
using Flit.Api.Authorization;
using Flit.Queries.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Analytics;

/// <summary>
/// Consultas de SuperAdmin sobre TODAS las compañías a la vez: el mismo constructor de
/// <c>/api/v1/analytics/queries</c>, pero sin un tenant único — operaciones puede armar «aprobados
/// Tesla», clonarla para Renting, o preguntar por varios organismos sin repetir la consulta por
/// compañía.
///
/// <para>Endpoints separados y no una rama dentro de <c>CompanyQueriesEndpoints</c> a propósito: el
/// flujo normal de una empresa no cambia una línea con este módulo, y el gate de acceso es una
/// policy declarativa (<see cref="AdminAuthorization.SuperAdminPolicy"/>), no un chequeo dentro del
/// handler.</para>
/// </summary>
public static class SuperAdminQueriesEndpoints
{
    public static IEndpointRouteBuilder MapSuperAdminQueriesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/analytics/superadmin-queries")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Analytics · Consultas de SuperAdmin (todas las compañías)");

        group.MapGet("/fields", GetFieldsAsync)
            .WithName("SuperAdminQueryFields")
            .WithSummary("Campos por los que se puede consultar, con «Compañía» resuelta")
            .Produces<IReadOnlyList<QueryFieldDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/run", RunAsync)
            .WithName("SuperAdminQueryRun")
            .WithSummary("Ejecuta una consulta sobre todas las compañías y devuelve una página")
            .WithDescription("Antes de cargar filas, cuenta cuántas habría — si ese universo real "
                + "supera el tope de cordura, se rechaza con 400 en vez de devolver una porción "
                + "arbitraria de la plataforma.")
            .Produces<CompanyQueryResultDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/saved", ListSavedAsync)
            .WithName("SuperAdminQuerySavedList")
            .WithSummary("Consultas guardadas por cualquier SuperAdmin, más las de fábrica")
            .WithDescription("Son de equipo, no personales: cualquier SuperAdmin ve, edita y borra "
                + "las de cualquier otro.")
            .Produces<IReadOnlyList<SavedQueryDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/saved", SaveAsync)
            .WithName("SuperAdminQuerySave")
            .WithSummary("Guarda una consulta nueva o actualiza una del equipo")
            .Produces<SavedQueryDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict);

        group.MapDelete("/saved/{id:guid}", DeleteSavedAsync)
            .WithName("SuperAdminQueryDelete")
            .WithSummary("Borra una consulta guardada del equipo")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    public sealed record RunRequest(QueryDefinition? Definition, int? Page, int? PageSize);

    public sealed record SaveRequest(
        Guid? Id,
        string? Nombre,
        string? Descripcion,
        QueryDefinition? Definition);

    private static async Task<IResult> RunAsync(
        HttpContext httpContext,
        [FromServices] ExecuteSuperAdminQueryHandler handler,
        [FromBody] RunRequest body,
        CancellationToken cancellationToken)
    {
        var request = ExecuteSuperAdminQueryHandler.BuildRequest(body?.Definition, body?.Page, body?.PageSize);

        try
        {
            var result = await handler.HandleAsync(request, cancellationToken).ConfigureAwait(false);
            return Results.Ok(result);
        }
        catch (SuperAdminQueryTooBroadException ex)
        {
            return Results.BadRequest(new { error = ex.Message, code = "CONSULTA_SIN_ACOTAR" });
        }
    }

    private static async Task<IResult> GetFieldsAsync(
        [FromServices] GetSuperAdminQueryFieldsHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> ListSavedAsync(
        [FromServices] ListSuperAdminSavedQueriesHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> SaveAsync(
        HttpContext httpContext,
        [FromServices] SaveSuperAdminQueryHandler handler,
        [FromBody] SaveRequest body,
        CancellationToken cancellationToken)
    {
        if (ResolveUser(httpContext.User) is not Guid userId)
        {
            return NoUser();
        }

        var input = SaveSuperAdminQueryHandler.BuildInput(body?.Nombre, body?.Descripcion, body?.Definition);

        try
        {
            var result = await handler.HandleAsync(userId, body?.Id, input, cancellationToken).ConfigureAwait(false);
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
        [FromServices] DeleteSuperAdminSavedQueryHandler handler,
        Guid id,
        CancellationToken cancellationToken)
    {
        var deleted = await handler.HandleAsync(id, cancellationToken).ConfigureAwait(false);

        return deleted
            ? Results.NoContent()
            : Results.NotFound(new { error = "La consulta no existe." });
    }

    private static Guid? ResolveUser(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue("sub"), out var sub) ? sub : null;

    private static IResult NoUser() =>
        Results.Json(
            new { error = "Token inválido: falta claim sub" },
            statusCode: StatusCodes.Status401Unauthorized);
}
