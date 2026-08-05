using System.Security.Claims;
using Flit.Admin.Application.OtQueries;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Domain.OtQueries;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Consultas del organismo: el usuario arma su propia búsqueda sobre los trámites, la guarda y la
/// exporta.
///
/// <para>La ejecución va por <c>POST</c> aunque sea una lectura. No es un descuido: la definición
/// lleva listas de placas pegadas desde Excel, y meterlas en la barra de direcciones las expondría
/// en los registros de los proxies y chocaría con el límite de longitud de URL a las pocas decenas.</para>
///
/// <para>Bajo la misma policy <c>OtModule</c> que el resto de <c>/admin/ot/*</c>, con el override
/// <c>transitOfficeId</c> para que SuperAdmin pueda mirar un organismo concreto.</para>
/// </summary>
public static class AdminOtQueriesEndpoints
{
    public static IEndpointRouteBuilder MapAdminOtQueriesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/ot/queries")
            .RequireAuthorization(AdminAuthorization.OtModulePolicy)
            .WithTags("Admin · OT · Consultas");

        group.MapGet("/fields", GetFieldsAsync)
            .WithName("AdminOtQueryFields")
            .WithSummary("Campos por los que se puede consultar")
            .WithDescription("El constructor de filtros se pinta a partir de esta respuesta, así que "
                + "un campo nuevo aparece en pantalla sin tocar el frontend. Las opciones de "
                + "'empresa' y 'revisor' vienen resueltas para este organismo.")
            .Produces<IReadOnlyList<OtQueryFieldDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/run", RunAsync)
            .WithName("AdminOtQueryRun")
            .WithSummary("Ejecuta una consulta y devuelve una página de trámites")
            .WithDescription("Una fila es un trámite: las condiciones sobre tablas hijas se resuelven "
                + "como 'existe' y nunca como cruce. Cuando la consulta lista placas, VIN o "
                + "radicados concretos, 'cobertura' dice qué pasó con cada valor pedido — si salió, "
                + "si no existe en el organismo, o qué filtro lo dejó fuera.")
            .Produces<OtQueryResultDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/saved", ListSavedAsync)
            .WithName("AdminOtQuerySavedList")
            .WithSummary("Consultas guardadas del usuario, más las de fábrica")
            .WithDescription("Las de fábrica van al final y con 'deFabrica' en true: no se persisten, "
                + "no se pueden editar y guardarlas las duplica.")
            .Produces<IReadOnlyList<OtSavedQueryDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/saved", SaveAsync)
            .WithName("AdminOtQuerySave")
            .WithSummary("Guarda una consulta nueva o actualiza una propia")
            .Produces<OtSavedQueryDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapDelete("/saved/{id:guid}", DeleteSavedAsync)
            .WithName("AdminOtQueryDelete")
            .WithSummary("Borra una consulta guardada del usuario")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>Cuerpo de la ejecución: la definición más la página que se quiere ver.</summary>
    public sealed record RunRequest(OtQueryDefinition? Definition, int? Page, int? PageSize);

    /// <summary>Cuerpo del guardado. <c>Id</c> nulo crea; con id actualiza la del propio usuario.</summary>
    public sealed record SaveRequest(
        Guid? Id,
        string? Nombre,
        string? Descripcion,
        OtQueryDefinition? Definition);

    private static async Task<IResult> RunAsync(
        HttpContext httpContext,
        [FromServices] ExecuteOtQueryHandler handler,
        [FromServices] ITransitOfficeCatalog transitOfficeCatalog,
        [FromBody] RunRequest body,
        CancellationToken cancellationToken,
        [FromQuery] Guid? transitOfficeId = null)
    {
        var (context, error) = ResolveContext(httpContext, transitOfficeCatalog, transitOfficeId);
        if (error is not null)
        {
            return error;
        }

        var request = ExecuteOtQueryHandler.BuildRequest(body?.Definition, body?.Page, body?.PageSize);

        var result = await handler
            .HandleAsync(context.TenantId, request, context.ScopedOfficeId, cancellationToken)
            .ConfigureAwait(false);

        return result is null ? NoTransitOffice() : Results.Ok(result);
    }

    private static async Task<IResult> GetFieldsAsync(
        HttpContext httpContext,
        [FromServices] GetOtQueryFieldsHandler handler,
        [FromServices] ITransitOfficeCatalog transitOfficeCatalog,
        CancellationToken cancellationToken,
        [FromQuery] Guid? transitOfficeId = null)
    {
        var (context, error) = ResolveContext(httpContext, transitOfficeCatalog, transitOfficeId);
        if (error is not null)
        {
            return error;
        }

        var result = await handler
            .HandleAsync(context.TenantId, context.ScopedOfficeId, cancellationToken)
            .ConfigureAwait(false);

        return result is null ? NoTransitOffice() : Results.Ok(result);
    }

    private static async Task<IResult> ListSavedAsync(
        HttpContext httpContext,
        [FromServices] ListOtSavedQueriesHandler handler,
        [FromServices] ITransitOfficeCatalog transitOfficeCatalog,
        CancellationToken cancellationToken,
        [FromQuery] Guid? transitOfficeId = null)
    {
        var (context, error) = ResolveContext(httpContext, transitOfficeCatalog, transitOfficeId);
        if (error is not null)
        {
            return error;
        }

        if (context.UserId is not Guid userId)
        {
            return NoUser();
        }

        var result = await handler
            .HandleAsync(context.TenantId, userId, context.ScopedOfficeId, cancellationToken)
            .ConfigureAwait(false);

        return result is null ? NoTransitOffice() : Results.Ok(result);
    }

    private static async Task<IResult> SaveAsync(
        HttpContext httpContext,
        [FromServices] SaveOtQueryHandler handler,
        [FromServices] ITransitOfficeCatalog transitOfficeCatalog,
        [FromBody] SaveRequest body,
        CancellationToken cancellationToken,
        [FromQuery] Guid? transitOfficeId = null)
    {
        var (context, error) = ResolveContext(httpContext, transitOfficeCatalog, transitOfficeId);
        if (error is not null)
        {
            return error;
        }

        if (context.UserId is not Guid userId)
        {
            return NoUser();
        }

        var input = SaveOtQueryHandler.BuildInput(body?.Nombre, body?.Descripcion, body?.Definition);

        try
        {
            var result = await handler
                .HandleAsync(context.TenantId, userId, body?.Id, input, context.ScopedOfficeId, cancellationToken)
                .ConfigureAwait(false);

            return result is null ? NoTransitOffice() : Results.Ok(result);
        }
        catch (OtSavedQueryNameTakenException ex)
        {
            return Results.Conflict(new { error = ex.Message, code = "NOMBRE_REPETIDO" });
        }
        catch (OtSavedQueryLimitException ex)
        {
            return Results.Conflict(new { error = ex.Message, code = "LIMITE_CONSULTAS" });
        }
    }

    private static async Task<IResult> DeleteSavedAsync(
        HttpContext httpContext,
        [FromServices] DeleteOtSavedQueryHandler handler,
        [FromServices] ITransitOfficeCatalog transitOfficeCatalog,
        Guid id,
        CancellationToken cancellationToken,
        [FromQuery] Guid? transitOfficeId = null)
    {
        var (context, error) = ResolveContext(httpContext, transitOfficeCatalog, transitOfficeId);
        if (error is not null)
        {
            return error;
        }

        if (context.UserId is not Guid userId)
        {
            return NoUser();
        }

        var deleted = await handler
            .HandleAsync(context.TenantId, userId, id, context.ScopedOfficeId, cancellationToken)
            .ConfigureAwait(false);

        return deleted
            ? Results.NoContent()
            : Results.NotFound(new { error = "La consulta no existe o no es suya." });
    }

    // ── Resolución común ──────────────────────────────────────────────────────────────────────

    private sealed record QueryContext(Guid TenantId, Guid? UserId, Guid? ScopedOfficeId);

    private static (QueryContext Context, IResult? Error) ResolveContext(
        HttpContext httpContext,
        ITransitOfficeCatalog transitOfficeCatalog,
        Guid? transitOfficeId)
    {
        var empty = new QueryContext(Guid.Empty, null, null);

        if (!Guid.TryParse(httpContext.User.FindFirstValue("tenant_id"), out var tenantId))
        {
            return (empty, Results.Json(
                new { error = "Token inválido: falta claim tenant_id" },
                statusCode: StatusCodes.Status401Unauthorized));
        }

        Guid? scopedOfficeId = null;
        if (IsSuperAdmin(httpContext.User) && transitOfficeId is Guid officeId && officeId != Guid.Empty)
        {
            if (!transitOfficeCatalog.Exists(officeId))
            {
                return (empty, Results.BadRequest(
                    new { error = "transitOfficeId no existe en el catálogo OT" }));
            }

            scopedOfficeId = officeId;
        }

        var userId = Guid.TryParse(httpContext.User.FindFirstValue("sub"), out var sub)
            ? sub
            : (Guid?)null;

        return (new QueryContext(tenantId, userId, scopedOfficeId), null);
    }

    private static IResult NoTransitOffice() =>
        Results.NotFound(new
        {
            error = "El usuario no tiene un organismo de tránsito asociado. "
                + "Configure el perfil OT del tenant para ver sus consultas.",
        });

    /// <summary>
    /// Las consultas guardadas son de una persona, así que sin identificarla no hay nada que
    /// listar ni dónde guardar. Consultar sí se puede: eso solo necesita el organismo.
    /// </summary>
    private static IResult NoUser() =>
        Results.Json(
            new { error = "Token inválido: falta claim sub" },
            statusCode: StatusCodes.Status401Unauthorized);

    private static bool IsSuperAdmin(ClaimsPrincipal user) =>
        user.IsInRole(AdminAuthorization.SuperAdminRole)
        || string.Equals(
            user.FindFirstValue(AdminAuthorization.RoleClaimType),
            AdminAuthorization.SuperAdminRole,
            StringComparison.OrdinalIgnoreCase);
}
