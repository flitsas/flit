using Flit.Api.Authorization;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// Reenvío ADMINISTRATIVO de la validación de identidad de un trámite (Feature #12155, HU #12161): a
/// diferencia de <c>POST /instances/{id}/biometric</c> (gateado a borrador/subsanación) y de
/// <c>POST /biometric-validations/{id}/resend</c> (exclusivo de la prevalidación standalone, rechaza
/// validaciones ligadas a trámite), este endpoint reenvía sobre un trámite YA <c>entregado</c> (o
/// cualquier otro estado en curso), opcionalmente actualizando el correo destinatario. Rutas y permisos
/// ya catalogados por HU #12157 (<see cref="AdminTramiteAuthorization"/>); esta HU implementa el endpoint
/// de negocio (<see cref="AdminReenviarValidacionIdentidadHandler"/>, que documenta por qué NO se
/// reutilizan esos dos mecanismos y qué SÍ se reutiliza de ellos).
/// </summary>
internal static class AdminReenviarValidacionEndpoints
{
    internal static IEndpointRouteBuilder MapAdminTramiteReenviarValidacionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/tramites").WithTags("Admin · Trámites (gestión avanzada)");

        // POST /api/v1/admin/tramites/{id}/validaciones-identidad/{validationId}/reenviar
        // AC1: sin `email` en el body, reenvía al correo actual. AC2: con `email`, lo persiste y reenvía al
        // nuevo. AC3: 'aprobado', 'anulado' o 'revocado' (string, HU #12165 aún no existe como enum) como
        // estado del trámite rechazan la acción (422).
        group.MapPost("/{id:guid}/validaciones-identidad/{validationId:guid}/reenviar", async (
            Guid id,
            Guid validationId,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            AdminReenviarValidacionRequest? request,
            HttpContext http,
            AdminReenviarValidacionIdentidadHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var command = new AdminReenviarValidacionIdentidadCommand(
                id, validationId, tenantId.Value, request?.Email, ResolveUserId(http.User));
            var (result, error, errorDetail, cooldownMinutos) = await handler.HandleAsync(command, ct);

            return error switch
            {
                null => result!.Queued
                    ? Results.Accepted($"/api/v1/tramites/instances/{id}/biometric/{validationId}", result)
                    : Results.Ok(result),
                TramiteEstadoErrores.NoEncontrado => Results.Problem(
                    statusCode: 404, title: "Not Found", detail: "Trámite o validación de identidad no encontrada."),
                AdminReenviarValidacionIdentidadHandler.IdentidadAprobada => Results.Problem(
                    statusCode: 409, title: error,
                    detail: "La identidad ya está aprobada. Para revalidar, inicia una validación nueva."),
                "reenvio_en_cooldown" => Results.Problem(
                    statusCode: 429, title: error,
                    detail: $"Espera {cooldownMinutos} minuto(s) antes de reenviar de nuevo."),
                "tope_reenvios" => Results.Problem(
                    statusCode: 429, title: error,
                    detail: "Se agotaron los reenvíos disponibles para esta validación."),
                "proveedor_error" => Results.Problem(
                    statusCode: 502, title: "Bad Gateway",
                    detail: "El proveedor de validación de identidad rechazó la solicitud."),
                "proveedor_no_disponible" => Results.Problem(
                    statusCode: 503, title: "Service Unavailable",
                    detail: "El proveedor de validación de identidad no está disponible. Reintenta más tarde."),
                _ => Results.Problem(
                    statusCode: 422, title: error,
                    detail: errorDetail ?? "No se pudo reenviar la validación de identidad."),
            };
        })
        .RequirePermission(AdminTramiteAuthorization.ReenviarValidacionSlug)
        .WithName("AdminTramiteReenviarValidacionIdentidad")
        .WithSummary("Reenvía la validación de identidad de un trámite, con correo opcional")
        .Produces<AdminReenviarValidacionIdentidadResult>(StatusCodes.Status200OK)
        .Produces<AdminReenviarValidacionIdentidadResult>(StatusCodes.Status202Accepted)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status422UnprocessableEntity)
        .Produces(StatusCodes.Status429TooManyRequests)
        .Produces(StatusCodes.Status502BadGateway)
        .Produces(StatusCodes.Status503ServiceUnavailable);

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
/// Body de <c>POST /api/v1/admin/tramites/{id}/validaciones-identidad/{validationId}/reenviar</c>.
/// <c>Email</c> es OPCIONAL: omitido o vacío ⇒ reenvía al correo actual (AC1); con valor ⇒ lo persiste y
/// reenvía al nuevo destinatario (AC2).
/// </summary>
internal sealed record AdminReenviarValidacionRequest(string? Email);
