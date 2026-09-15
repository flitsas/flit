using System.Security.Claims;
using Flit.Tramites.Application.UseCases.RevocationRequests;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.RevocationRequests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// Solicitud de revocatoria de un trámite Aprobado (HU #12572, Feature #12565): motivo + documento de
/// soporte (PDF) + los dos checks de confirmación (AC1, copy en HU #12574). Autor: el Administrador
/// de compañía dueño del trámite (autenticado, tenant-scoped como el resto de
/// <c>/api/v1/tramites/instances/{id}/...</c> — no es una acción cross-tenant de OT/SuperAdmin, así
/// que no vive bajo <c>/api/v1/admin/tramites</c> ni exige un permiso RBAC dedicado).
/// </summary>
internal static class RevocationRequestEndpoints
{
    internal static IEndpointRouteBuilder MapTramitesRevocationRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tramites").WithTags("Trámites · Revocatoria");

        // POST /api/v1/tramites/instances/{id}/revocation-requests (multipart/form-data:
        // reason + confirmAccuracy + confirmConsequences + file) -> 201 RequestRevocationResult
        group.MapPost("/instances/{id:guid}/revocation-requests", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            [FromForm] string? reason,
            [FromForm] string? confirmAccuracy,
            [FromForm] string? confirmConsequences,
            IFormFile? file,
            HttpContext http,
            RequestRevocationHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var userId = ResolveUserId(http.User);
            if (userId is null)
                return Results.Problem(statusCode: 401, title: "Unauthorized", detail: "No se pudo resolver el usuario autenticado.");

            await using var stream = file?.OpenReadStream();
            var supportDocument = file is { Length: > 0 } && stream is not null
                ? new RevocationSupportDocumentInput(file.FileName, file.ContentType, file.Length, stream)
                : null;

            var command = new RequestRevocationCommand(
                id,
                tenantId.Value,
                reason,
                ParseBool(confirmAccuracy),
                ParseBool(confirmConsequences),
                supportDocument,
                userId.Value);

            var (result, error, errorDetail) = await handler.HandleAsync(command, ct);

            return error switch
            {
                null => Results.Created($"/api/v1/tramites/instances/{id}/revocation-requests/{result!.Id}", result),
                TramiteEstadoErrores.NoEncontrado => Results.Problem(
                    statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                RequestRevocationHandler.TramiteNoAprobado => Results.Problem(
                    statusCode: 409, title: error, detail: errorDetail),
                RevocationRequestGate.SolicitudActivaExistente => Results.Problem(
                    statusCode: 409, title: error, detail: errorDetail),
                _ => Results.Problem(statusCode: 422, title: error, detail: errorDetail),
            };
        })
        .WithName("RequestProcedureInstanceRevocation")
        .WithSummary("Solicita la revocatoria de un trámite Aprobado")
        .Produces(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status422UnprocessableEntity)
        .DisableAntiforgery();

        return app;
    }

    /// <summary>
    /// Los checks de confirmación viajan como texto en el multipart (no hay binding nativo de
    /// <c>bool</c> para <c>[FromForm]</c> que tolere el campo ausente sin fallar el binding): acepta
    /// "true"/"false" case-insensitive; cualquier otro valor (incluido ausente) cuenta como no
    /// marcado, igual que un checkbox sin marcar.
    /// </summary>
    private static bool ParseBool(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Usuario autenticado (claim <c>sub</c>), para <c>requested_by</c> (NOT NULL).</summary>
    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
