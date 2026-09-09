using Flit.Api.Authorization;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.ReadModels;
using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// Reasignación ADMINISTRATIVA del gestor de un trámite (Feature #12155, HU #12162): cambia
/// <c>AssignedToUserId</c> (gestor HOY responsable) a otro usuario DISPONIBLE del mismo tenant, sin
/// tocar <c>CreatedByUserId</c> (quién radicó, auditoría inmutable). Rutas y permisos ya catalogados
/// por HU #12157 (<see cref="AdminTramiteAuthorization"/>); esta HU implementa el endpoint de negocio
/// (<see cref="AdminReasignarGestorHandler"/>, que documenta la colisión terminológica de "gestor" con
/// el listado del dashboard) y el endpoint de solo lectura que alimenta el selector del frontend
/// (HU #12163).
/// </summary>
internal static class AdminReasignarGestorEndpoints
{
    internal static IEndpointRouteBuilder MapAdminTramiteReasignarGestorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/tramites").WithTags("Admin · Trámites (gestión avanzada)");

        // POST /api/v1/admin/tramites/{id}/reasignar-gestor — AC1: cambia AssignedToUserId, conserva
        // CreatedByUserId. AC2: el destino debe existir, pertenecer al tenant, estar activo y sin
        // suspensión vigente (422 en cualquier otro caso). AC3: queda en el historial (evento propio).
        group.MapPost("/{id:guid}/reasignar-gestor", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            AdminReasignarGestorRequest? request,
            HttpContext http,
            AdminReasignarGestorHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");
            if (request?.NewAssignedToUserId is null || request.NewAssignedToUserId == Guid.Empty)
                return Results.Problem(
                    statusCode: 400, title: "Bad Request",
                    detail: "Falta el usuario destino (newAssignedToUserId).");

            var command = new AdminReasignarGestorCommand(
                id, tenantId.Value, request.NewAssignedToUserId.Value, ResolveUserId(http.User));
            var (result, error, errorDetail) = await handler.HandleAsync(command, ct);

            return error switch
            {
                null => Results.Ok(result),
                TramiteEstadoErrores.NoEncontrado => Results.Problem(
                    statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                TramiteEstadoErrores.ConflictoConcurrencia => Results.Problem(
                    statusCode: 409, title: error,
                    detail: errorDetail ?? "El trámite fue modificado por otro proceso. Recargue e intente de nuevo."),
                _ => Results.Problem(
                    statusCode: 422, title: error,
                    detail: errorDetail ?? "No se pudo reasignar el gestor del trámite."),
            };
        })
        .RequirePermission(AdminTramiteAuthorization.ReasignarGestorSlug)
        .WithName("AdminTramiteReasignarGestor")
        .WithSummary("Reasigna el gestor responsable de un trámite a otro usuario disponible del tenant")
        .Produces<AdminReasignarGestorResult>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status422UnprocessableEntity);

        // GET /api/v1/admin/tramites/gestores-disponibles — AC-selector: usuarios activos, no
        // eliminados, sin suspensión vigente, del tenant del header. Alimenta el selector de
        // reasignación del frontend (HU #12163). Mismo permiso que la acción de reasignar: no se
        // reutiliza GET /api/v1/security/users (mezcla invitaciones pendientes, no filtra por
        // disponibilidad y está gateado por un permiso de administración de usuarios distinto).
        group.MapGet("/gestores-disponibles", async (
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            ListGestoresDisponiblesHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var result = await handler.HandleAsync(tenantId.Value, ct);
            return Results.Ok(result);
        })
        .RequirePermission(AdminTramiteAuthorization.ReasignarGestorSlug)
        .WithName("AdminTramiteGestoresDisponibles")
        .WithSummary("Lista los gestores disponibles del tenant para el selector de reasignación")
        .Produces<IReadOnlyList<GestorOption>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    /// <summary>Usuario autenticado (claim <c>sub</c>), para la trazabilidad en <c>ProcedureInstanceEvent</c>.</summary>
    private static Guid? ResolveUserId(System.Security.Claims.ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}

/// <summary>Body de <c>POST /api/v1/admin/tramites/{id}/reasignar-gestor</c>.</summary>
internal sealed record AdminReasignarGestorRequest(Guid? NewAssignedToUserId);
