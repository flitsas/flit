using Flit.Api.Authorization;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// Anulación administrativa de un trámite (Feature #12155, HU #12160): mueve el trámite a
/// <c>anulado</c> desde CUALQUIER estado, salvo dos excepciones que son autoridad exclusiva del
/// organismo de tránsito — <c>aprobado</c> (422, <see cref="TramiteEstadoErrores.CannotAnnulApproved"/>)
/// y <c>revocado</c> (422, <see cref="TramiteEstadoErrores.CannotAnnulRevoked"/>; ese estado todavía no
/// existe en el dominio, lo agrega la Feature hermana #12156, HU #12165 — ver
/// <see cref="AdminAnularHandler"/>). Rutas y permisos ya catalogados por HU #12157
/// (<see cref="AdminTramiteAuthorization"/>); esta HU implementa el endpoint de negocio.
/// </summary>
internal static class AdminAnularEndpoints
{
    internal static IEndpointRouteBuilder MapAdminTramiteAnularEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/tramites").WithTags("Admin · Trámites (gestión avanzada)");

        // POST /api/v1/admin/tramites/{id}/anular — AC1: anula desde cualquier estado. AC2/AC3:
        // 'aprobado' y 'revocado' (string, HU #12165 aún no existe como enum) rechazados como origen (422).
        group.MapPost("/{id:guid}/anular", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            AdminAnularRequest? request,
            HttpContext http,
            AdminAnularHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var command = new AdminAnularCommand(
                id, tenantId.Value, request?.Reason, ResolveUserId(http.User));
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
                    detail: errorDetail ?? "No se pudo anular el trámite."),
            };
        })
        .RequirePermission(AdminTramiteAuthorization.AnularSlug)
        .WithName("AdminTramiteAnular")
        .WithSummary("Anula un trámite desde cualquier estado (excluye 'aprobado' y 'revocado')")
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
/// Body de <c>POST /api/v1/admin/tramites/{id}/anular</c>. Ambos campos opcionales: no hay campo
/// obligatorio a diferencia de <c>AdminCambiarEstadoRequest</c> porque el destino es fijo (anulado) y
/// el motivo es libre (no se exige como en RF05 de las transiciones normales — ver
/// <see cref="AdminAnularHandler"/>).
/// </summary>
internal sealed record AdminAnularRequest(string? Reason);
