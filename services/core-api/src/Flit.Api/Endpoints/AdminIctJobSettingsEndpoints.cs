using System.Security.Claims;
using Flit.Admin.Application.Ict;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Configuración operativa de jobs ICT (<c>ict.job_settings</c>, HU #12512).
/// SuperAdmin: la fila es GLOBAL de plataforma (sin tenant, sin secretos).
/// Prefijo <c>/api/v1/admin/ict</c> — no <c>/api/v1/ict</c>, que el Gateway enruta a core-ict.
/// </summary>
public static class AdminIctJobSettingsEndpoints
{
    public static IEndpointRouteBuilder MapAdminIctJobSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/ict")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Admin · ICT · Jobs");

        group.MapGet("/job-settings", GetAsync)
            .WithName("AdminIctGetJobSettings")
            .WithSummary("Configuración vigente de cadencia/lote/concurrencia ICT")
            .Produces<IctJobSettingsView>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPut("/job-settings", PutAsync)
            .WithName("AdminIctPutJobSettings")
            .WithSummary("Actualiza ict.job_settings; core-ict la aplica en caliente")
            .Produces<IctJobSettingsView>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    public sealed record PutIctJobSettingsRequest(
        int WindowStartHour,
        int WindowEndHour,
        int BusinessPollSeconds,
        int ExternalPollSeconds,
        int OrchestratorPollSeconds,
        int OrchestratorConcurrency,
        int OrchestratorBatchSize,
        int SendPollSeconds,
        int SendConcurrency,
        int SendBatchSize,
        int WebhookPollSeconds,
        int WebhookBatchSize,
        int BusinessBatchSize,
        int ExternalBatchSize);

    private static async Task<IResult> GetAsync(
        [FromServices] GetIctJobSettingsHandler handler,
        CancellationToken cancellationToken)
    {
        var view = await handler.HandleAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(view);
    }

    private static async Task<IResult> PutAsync(
        [FromBody] PutIctJobSettingsRequest? request,
        ClaimsPrincipal user,
        [FromServices] SaveIctJobSettingsHandler handler,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Results.BadRequest(new
            {
                error = "configuracion_invalida",
                errors = Array.Empty<object>(),
            });
        }

        var result = await handler.HandleAsync(
            new SaveIctJobSettingsCommand(
                request.WindowStartHour,
                request.WindowEndHour,
                request.BusinessPollSeconds,
                request.ExternalPollSeconds,
                request.OrchestratorPollSeconds,
                request.OrchestratorConcurrency,
                request.OrchestratorBatchSize,
                request.SendPollSeconds,
                request.SendConcurrency,
                request.SendBatchSize,
                request.WebhookPollSeconds,
                request.WebhookBatchSize,
                request.BusinessBatchSize,
                request.ExternalBatchSize,
                ResolveUserId(user)),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsValid)
        {
            return Results.BadRequest(new
            {
                error = "configuracion_invalida",
                errors = result.Errors.Select(e => new { field = e.Field, message = e.Message }),
            });
        }

        return Results.Ok(result.Settings);
    }

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
