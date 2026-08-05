using System.Security.Claims;
using Flit.Admin.Application.RejectionReasons;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Catálogo global de causales de rechazo.
///
/// <para>La escritura exige SuperAdmin (el catálogo es global: si cada organismo definiera el suyo,
/// el reporte de motivos dejaría de ser comparable entre organismos y entre empresas). La LECTURA
/// queda abierta a cualquier usuario autenticado porque la consumen dos perfiles distintos: el
/// revisor del organismo, que necesita la lista para poder rechazar, y la consola de reportes, que
/// la usa para etiquetar los motivos.</para>
/// </summary>
public static class AdminRejectionReasonsEndpoints
{
    private const string BasePath = "/api/v1/admin/rejection-reasons";

    public static IEndpointRouteBuilder MapAdminRejectionReasonsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Lectura: autenticado. Sin esto el revisor del organismo no podría abrir el modal de
        // rechazo, que es el flujo que da sentido al catálogo.
        var read = app
            .MapGroup(BasePath)
            .RequireAuthorization()
            .WithTags("Admin · Causales de rechazo");

        read.MapGet("/", ListAsync)
            .WithName("AdminRejectionReasonList")
            .WithSummary("Lista las causales de rechazo del catálogo")
            .WithDescription("Filtra por modalidad (matricula_inicial | traspaso). Por defecto solo "
                + "activas; includeInactive=true incluye las retiradas (necesario en la consola de "
                + "administración para poder reactivarlas).")
            .Produces<IReadOnlyList<RejectionReasonResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        // Escritura: SuperAdmin.
        var write = app
            .MapGroup(BasePath)
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Admin · Causales de rechazo");

        write.MapPost("/", CreateAsync)
            .WithName("AdminRejectionReasonCreate")
            .WithSummary("Crea una causal de rechazo")
            .Produces<RejectionReasonResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        write.MapPut("/{id:guid}", UpdateAsync)
            .WithName("AdminRejectionReasonUpdate")
            .WithSummary("Actualiza una causal de rechazo")
            .Produces<RejectionReasonResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        write.MapPatch("/{id:guid}/active", SetActiveAsync)
            .WithName("AdminRejectionReasonSetActive")
            .WithSummary("Activa o retira una causal de rechazo")
            .WithDescription("No hay borrado: una causal retirada debe seguir resolviendo el nombre "
                + "de los rechazos históricos que la usaron.")
            .Produces<RejectionReasonResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListAsync(
        [FromServices] ListRejectionReasonsHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] string? modalidad = null,
        [FromQuery] bool? includeInactive = null)
    {
        var result = await handler
            .HandleAsync(modalidad, includeInactive ?? false, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static async Task<IResult> CreateAsync(
        CreateRejectionReasonRequest request,
        HttpContext httpContext,
        [FromServices] CreateRejectionReasonHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(request, ResolveUserId(httpContext.User), cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            RejectionReasonOutcome.Ok => Results.Created(
                $"{BasePath}/{result.Reason!.Id}", result.Reason),
            _ => Results.Json(
                new { error = result.Error }, statusCode: StatusCodes.Status422UnprocessableEntity),
        };
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateRejectionReasonRequest request,
        HttpContext httpContext,
        [FromServices] UpdateRejectionReasonHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(id, request, ResolveUserId(httpContext.User), cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            RejectionReasonOutcome.Ok => Results.Ok(result.Reason),
            RejectionReasonOutcome.ValidationFailed => Results.Json(
                new { error = result.Error }, statusCode: StatusCodes.Status422UnprocessableEntity),
            _ => Results.NotFound(new { error = $"No existe la causal {id}." }),
        };
    }

    private static async Task<IResult> SetActiveAsync(
        Guid id,
        SetRejectionReasonActiveRequest request,
        HttpContext httpContext,
        [FromServices] SetRejectionReasonActiveHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await handler
            .HandleAsync(id, request.IsActive, ResolveUserId(httpContext.User), cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            RejectionReasonOutcome.Ok => Results.Ok(result.Reason),
            _ => Results.NotFound(new { error = $"No existe la causal {id}." }),
        };
    }

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
