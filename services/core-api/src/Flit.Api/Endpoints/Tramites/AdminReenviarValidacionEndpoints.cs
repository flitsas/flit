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
            var (result, error, errorDetail, _) = await handler.HandleAsync(command, ct);

            if (error is null)
            {
                return result!.Queued
                    ? Results.Accepted($"/api/v1/tramites/instances/{id}/biometric/{validationId}", result)
                    : Results.Ok(result);
            }

            var (status, title, detail) = DescribirError(error, errorDetail);
            return Results.Problem(statusCode: status, title: title, detail: detail);
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

        // GET /api/v1/admin/tramites/{id}/validaciones-identidad/reenviables
        // HU #13821 (Épica #13817) — una entrada por PERSONA que no ha firmado, con la validación propia sobre
        // la que se reenvía. Sin firmadas, sin baúl y sin identidades reutilizadas de otro trámite.
        group.MapGet("/{id:guid}/validaciones-identidad/reenviables", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            ListValidacionesReenviablesHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ct);
            return error is null
                ? Results.Ok(result)
                : Results.Problem(statusCode: 404, title: "Not Found", detail: "Trámite no encontrado.");
        })
        .RequirePermission(AdminTramiteAuthorization.ReenviarValidacionSlug)
        .WithName("AdminTramiteListValidacionesReenviables")
        .WithSummary("Personas del trámite a las que se puede reenviar la validación de identidad")
        .Produces<ValidacionesReenviablesResult>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        // POST /api/v1/admin/tramites/{id}/validaciones-identidad/reenviar
        // HU #13821 — reenvío a una o varias personas en un solo paso. 200 con el resultado POR PERSONA (el
        // fallo de una no frena a las demás); el trámite aprobado/anulado/revocado rechaza toda la solicitud.
        group.MapPost("/{id:guid}/validaciones-identidad/reenviar", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            AdminReenviarValidacionesLoteRequest? request,
            HttpContext http,
            AdminReenviarValidacionesLoteHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var items = (request?.Items ?? [])
                .Select(i => new AdminReenviarValidacionLoteItem(i.ValidationId, i.Email))
                .ToList();
            var (result, error, errorDetail) = await handler.HandleAsync(
                new AdminReenviarValidacionesLoteCommand(id, tenantId.Value, items, ResolveUserId(http.User)), ct);

            if (error is not null)
            {
                var (status, title, detail) = error is AdminReenviarValidacionesLoteHandler.SolicitudVacia
                    or AdminReenviarValidacionesLoteHandler.DemasiadasPersonas
                    ? (400, error, errorDetail ?? "Solicitud inválida.")
                    : DescribirError(error, errorDetail);
                return Results.Problem(statusCode: status, title: title, detail: detail);
            }

            return Results.Ok(new AdminReenviarValidacionesLoteResponse(result!.Items
                .Select(i => i.Error is null
                    ? new AdminReenviarValidacionLoteItemResponse(
                        i.ValidationId, true, i.Result!.Queued, i.Result.Queued ? null : i.Result.CaptureUrl,
                        i.Result.EmailActualizado, i.Result.Validation.Email, null, null)
                    : new AdminReenviarValidacionLoteItemResponse(
                        i.ValidationId, false, false, null, false, null, i.Error, DescribirError(i.Error, i.ErrorDetail).Detail))
                .ToList()));
        })
        .RequirePermission(AdminTramiteAuthorization.ReenviarValidacionSlug)
        .WithName("AdminTramiteReenviarValidacionesIdentidad")
        .WithSummary("Reenvía la validación de identidad a una o varias personas del trámite, con correo opcional por persona")
        .Produces<AdminReenviarValidacionesLoteResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    /// <summary>
    /// Código de error del reenvío → respuesta HTTP y mensaje para el usuario. Compartido por el reenvío
    /// individual (como Problem) y el múltiple (como mensaje por persona), para que digan lo mismo.
    /// </summary>
    private static (int Status, string Title, string Detail) DescribirError(string error, string? errorDetail) => error switch
    {
        TramiteEstadoErrores.NoEncontrado => (404, "Not Found", "Trámite o validación de identidad no encontrada."),
        AdminReenviarValidacionIdentidadHandler.IdentidadAprobada => (409, error,
            "La identidad ya está aprobada. Para revalidar, inicia una validación nueva."),
        // HU #13286 — la validación es del flujo manual: no se reenvía a Kyverum.
        "identidad_manual" => (409, error, "La validación está en el flujo manual: no se reenvía a Kyverum."),
        "validacion_en_curso" => (409, error,
            "Ya existe otra validación de identidad en curso para este mismo documento. Espera a que finalice antes de reenviar."),
        "proveedor_error" => (502, "Bad Gateway", "El proveedor de validación de identidad rechazó la solicitud."),
        "proveedor_no_disponible" => (503, "Service Unavailable",
            "El proveedor de validación de identidad no está disponible. Reintenta más tarde."),
        _ => (422, error, errorDetail ?? "No se pudo reenviar la validación de identidad."),
    };

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

/// <summary>
/// Body de <c>POST /api/v1/admin/tramites/{id}/validaciones-identidad/reenviar</c> (HU #13821): una entrada
/// por persona; <c>Email</c> opcional por persona, misma semántica que el reenvío individual.
/// </summary>
internal sealed record AdminReenviarValidacionesLoteRequest(IReadOnlyList<AdminReenviarValidacionLoteItemRequest>? Items);

internal sealed record AdminReenviarValidacionLoteItemRequest(Guid ValidationId, string? Email);

/// <summary>
/// Resultado por persona. <c>CaptureUrl</c> solo cuando el enlace ya existe (no en cola). <c>Mensaje</c> es el
/// texto para el usuario cuando <c>Ok</c> es false.
/// </summary>
internal sealed record AdminReenviarValidacionLoteItemResponse(
    Guid ValidationId,
    bool Ok,
    bool Queued,
    string? CaptureUrl,
    bool EmailActualizado,
    string? Email,
    string? Error,
    string? Mensaje);

internal sealed record AdminReenviarValidacionesLoteResponse(IReadOnlyList<AdminReenviarValidacionLoteItemResponse> Items);
