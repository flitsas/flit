using Flit.Api.Authorization;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// Cambio de estado administrativo de un trámite (Feature #12155, HU #12159): mueve el trámite a
/// CUALQUIER estado de negocio conocido sin las restricciones de <c>TramiteStateMachine</c>, salvo
/// la única regla dura — <c>aprobado</c> nunca participa, ni como origen ni como destino (AC2/AC3).
/// Rutas y permisos ya catalogados por HU #12157 (<see cref="AdminTramiteAuthorization"/>); esta HU
/// implementa el endpoint de negocio (<see cref="AdminCambiarEstadoHandler"/>).
/// </summary>
internal static class AdminEstadoEndpoints
{
    internal static IEndpointRouteBuilder MapAdminTramiteEstadoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/tramites").WithTags("Admin · Trámites (gestión avanzada)");

        // POST /api/v1/admin/tramites/{id}/estado — AC1: cambio directo, sin pasar por
        // TramiteStateMachine.IsValidTransition. AC2/AC3: 'aprobado' rechazado como destino u origen (422).
        group.MapPost("/{id:guid}/estado", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            AdminCambiarEstadoRequest request,
            HttpContext http,
            AdminCambiarEstadoHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");
            if (string.IsNullOrWhiteSpace(request.ToStatus))
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta el estado destino (toStatus).");

            var command = new AdminCambiarEstadoCommand(
                id, tenantId.Value, request.ToStatus, request.Reason, ResolveUserId(http.User));
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
                    detail: errorDetail ?? "No se pudo cambiar el estado del trámite."),
            };
        })
        .RequirePermission(AdminTramiteAuthorization.CambiarEstadoSlug)
        .WithName("AdminTramiteCambiarEstado")
        .WithSummary("Cambia el estado de un trámite sin restricciones de flujo (excluye 'aprobado')")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status422UnprocessableEntity);

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

/// <summary>
/// Body de <c>POST /api/v1/admin/tramites/{id}/estado</c>. <c>ToStatus</c> es el único campo
/// obligatorio; <c>Reason</c> es libre (no se exige como en RF05 de las transiciones normales,
/// porque este endpoint no reutiliza esas reglas — ver <see cref="AdminCambiarEstadoHandler"/>).
/// </summary>
internal sealed record AdminCambiarEstadoRequest(string? ToStatus, string? Reason);
