using System.Security.Claims;
using Flit.Admin.Application.Auditing;
using Flit.Api.Authorization;
using Flit.Tramites.Application.UseCases.TermsAcceptance;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// Aceptación de Términos y Condiciones antes de crear un trámite (Epic #12543). Exige usuario
/// autenticado (cualquier JWT válido): si puede radicar, puede aceptar. El tenant lo impone
/// <c>TenantEnforcementMiddleware</c> (ruta en <c>RuntimeScopedRoutes</c>): el de la compañía
/// sale del token; el SuperAdmin acota con <c>X-Tenant-Id</c> o queda sin tenant.
///
/// El frontend llama este POST al pulsar Continuar y SOLO con 201 muestra el formulario (RN-03):
/// cualquier otro código deja el paso bloqueado.
/// </summary>
public static class TermsAcceptanceEndpoints
{
    public const string Route = "/api/v1/tramites/terms-acceptances";

    public static IEndpointRouteBuilder MapTramitesTermsAcceptanceEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost(Route, RecordAsync)
            .RequireAuthorization()
            .WithName("TramitesRecordTermsAcceptance")
            .WithSummary("Registra que el usuario aceptó los Términos y Condiciones para crear un trámite")
            .Produces<ProcedureTermsAcceptanceResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        // El asistente necesita saber qué documento enlazar sin duplicar la URL en el cliente.
        app.MapGet(Route + "/current", (ProcedureTermsOptions options) =>
                Results.Ok(new ProcedureTermsResponse(options.Url)))
            .RequireAuthorization()
            .WithName("TramitesGetCurrentTerms")
            .WithSummary("URL del documento de Términos y Condiciones vigente")
            .Produces<ProcedureTermsResponse>(StatusCodes.Status200OK);

        return app;
    }

    private static async Task<IResult> RecordAsync(
        RecordProcedureTermsAcceptanceRequest? request,
        HttpContext httpContext,
        [FromServices] RecordProcedureTermsAcceptanceHandler handler,
        [FromServices] IAuditContextAccessor auditContext,
        CancellationToken cancellationToken)
    {
        var userId = ResolveUserId(httpContext.User);
        if (userId is null)
        {
            return Results.Json(
                new ErrorResponse("No fue posible resolver el usuario del token."),
                statusCode: StatusCodes.Status400BadRequest);
        }

        var command = new RecordProcedureTermsAcceptanceCommand(
            TenantId: RequestTenantResolver.FromItems(httpContext).TenantId,
            UserId: userId.Value,
            ProcedureTypeCode: request?.ProcedureTypeCode ?? string.Empty,
            ClientIp: auditContext.ClientIp,
            UserAgent: httpContext.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null);

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.Outcome switch
        {
            RecordProcedureTermsAcceptanceOutcome.Recorded => Results.Created(
                $"{Route}/{result.Acceptance!.Id}",
                new ProcedureTermsAcceptanceResponse(
                    result.Acceptance.Id,
                    result.Acceptance.ProcedureTypeCode,
                    result.Acceptance.TermsUrl,
                    result.Acceptance.AcceptedAt)),
            RecordProcedureTermsAcceptanceOutcome.InvalidProcedureTypeCode => Results.Json(
                new ErrorResponse("Indique el tipo de trámite (procedureTypeCode)."),
                statusCode: StatusCodes.Status400BadRequest),
            RecordProcedureTermsAcceptanceOutcome.ProcedureTypeNotFound => Results.Json(
                new ErrorResponse($"No existe el tipo de trámite {command.ProcedureTypeCode.Trim()}."),
                statusCode: StatusCodes.Status422UnprocessableEntity),
            _ => Results.Json(
                new ErrorResponse("No fue posible registrar la aceptación."),
                statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue("sub") ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var userId) ? userId : null;
    }

    /// <summary>Body del POST: el code del tipo que el usuario está por crear.</summary>
    public sealed record RecordProcedureTermsAcceptanceRequest(string? ProcedureTypeCode);

    public sealed record ProcedureTermsAcceptanceResponse(Guid Id, string ProcedureTypeCode, string TermsUrl, DateTimeOffset AcceptedAt);

    public sealed record ProcedureTermsResponse(string Url);

    /// <summary>Cuerpo de error simple: <c>{ error: "mensaje" }</c>, igual que el resto de /tramites.</summary>
    private sealed record ErrorResponse(string Error);
}
