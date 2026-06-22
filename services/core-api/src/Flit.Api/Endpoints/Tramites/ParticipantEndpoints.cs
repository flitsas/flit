using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// Endpoints autenticados del portal de participantes (Slice 7 Part B): invitar/listar/reinvitar.
/// La invitación devuelve el token CRUDO una sola vez (magic-link); en BD solo se persiste su hash.
/// El portal público (consent/documentos/firma/finalizar) vive en PublicPortalEndpoints.
/// </summary>
internal static class ParticipantEndpoints
{
    internal static IEndpointRouteBuilder MapTramitesParticipantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tramites");

        // POST invitar participante -> 201 { participant, token, magicLinkPath }
        group.MapPost("/instances/{id:guid}/participants", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            [FromBody] InvitarParticipanteInput? body,
            InvitarParticipanteHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");
            if (body is null)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta el cuerpo de la solicitud.");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, body, null, ct);
            return error switch
            {
                "datos_incompletos" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "Completa nombre y email."),
                "rol_invalido" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "rol inválido (use comprador|vendedor|mandatario)."),
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "participante_activo" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Ya existe un participante activo para este rol."),
                _ => Results.Created($"/api/v1/tramites/instances/{id}/participants/{result!.Participant.Id}", result),
            };
        }).WithName("InvitarProcedureInstanceParticipant");

        // GET lista de participantes -> { participants: [...] }
        group.MapGet("/instances/{id:guid}/participants", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            ListParticipantesHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found.")
                : Results.Ok(result);
        }).WithName("ListProcedureInstanceParticipants");

        // POST reinvitar (rota token + reinicia expiración, con cooldown 24h) -> 200 { participant, token, magicLinkPath }
        group.MapPost("/instances/{id:guid}/participants/{pid:guid}/reinvite", async (
            Guid id,
            Guid pid,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            ReinvitarParticipanteHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, pid, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "participant_not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Participante no encontrado."),
                "ya_completado" => Results.Problem(statusCode: 409, title: "Conflict", detail: "El participante ya finalizó su parte."),
                "reminder_cooldown" => Results.Problem(statusCode: 429, title: "Too Many Requests", detail: "Espera 24h antes de reenviar el recordatorio."),
                _ => Results.Ok(result),
            };
        }).WithName("ReinvitarProcedureInstanceParticipant");

        return app;
    }
}
