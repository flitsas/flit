using System.Security.Claims;
using Flit.Api.Authorization;
using Flit.Tramites.Application.UseCases.RuntConfirmation;
using Flit.Tramites.Domain.RuntConfirmation;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Administración → Plataforma → Confirmación RUNT (Epic #12234, Feature #12276).
/// Se autoriza por PERMISO y nunca por <c>SuperAdminPolicy</c>: es el único submódulo de Plataforma
/// que se puede delegar (HU #12313). Configuración: <c>runt_confirmation.settings.manage</c>.
/// </summary>
public static class AdminRuntConfirmationEndpoints
{
    public const string SettingsManagePermission = "runt_confirmation.settings.manage";
    public const string HistoryReadPermission = "runt_confirmation.history.read";

    public static IEndpointRouteBuilder MapAdminRuntConfirmationEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/runt-confirmation")
            .WithTags("Admin · Plataforma · Confirmación RUNT");

        // ── Configuración global (HU #12277) ────────────────────────────────────────────────
        group.MapGet("/settings", GetSettingsAsync)
            .RequirePermission(SettingsManagePermission)
            .WithName("AdminRuntConfirmationGetSettings")
            .WithSummary("Configuración global de la Confirmación RUNT (fila única)")
            .Produces<RuntConfirmationSettingsDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPut("/settings", PutSettingsAsync)
            .RequirePermission(SettingsManagePermission)
            .WithName("AdminRuntConfirmationPutSettings")
            .WithSummary("Guarda la configuración global; audita cada campo cambiado")
            .Produces<RuntConfirmationSettingsDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // ── Historial (HU #12310): intentos, detalle, crudo, corridas y consulta manual ────
        group.MapGet("/attempts", ListAttemptsAsync)
            .RequirePermission(HistoryReadPermission)
            .WithName("AdminRuntConfirmationListAttempts")
            .WithSummary("Intentos de confirmación con filtros y paginación (fecha desc)")
            .Produces<RuntConfirmationAttemptsPage>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/attempts/{id:guid}", GetAttemptAsync)
            .RequirePermission(HistoryReadPermission)
            .WithName("AdminRuntConfirmationGetAttempt")
            .WithSummary("Detalle de un intento: motivo, versión de la regla, proveedor y referencias al crudo")
            .Produces<RuntConfirmationAttemptDetail>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/attempts/{id:guid}/raw", GetAttemptRawAsync)
            .RequirePermission(HistoryReadPermission)
            .WithName("AdminRuntConfirmationGetAttemptRaw")
            .WithSummary("Respuesta cruda del proveedor tal como llegó (sanitizada de credenciales al guardarse)")
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/runs", ListRunsAsync)
            .RequirePermission(HistoryReadPermission)
            .WithName("AdminRuntConfirmationListRuns")
            .WithSummary("Corridas (programadas y manuales), la más reciente primero")
            .Produces<RuntConfirmationRunsPage>(StatusCodes.Status200OK);

        group.MapGet("/runs/latest", GetLatestRunAsync)
            .RequirePermission(HistoryReadPermission)
            .WithName("AdminRuntConfirmationLatestRun")
            .WithSummary("La corrida más reciente, para el resumen de la pestaña Configuración")
            .Produces<RuntConfirmationRun>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/procedures/{id:guid}/consult-now", ConsultNowAsync)
            .RequirePermission(HistoryReadPermission)
            .WithName("AdminRuntConfirmationConsultNow")
            .WithSummary("Consulta manual inmediata de un trámite aprobado con el proveedor configurado")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> ListAttemptsAsync(
        [FromQuery] Guid? runId,
        [FromQuery] string? verdict,
        [FromQuery] string? procedureTypeCode,
        [FromQuery] Guid? procedureInstanceId,
        [FromQuery] string? search,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        [FromServices] ListRuntConfirmationAttemptsHandler handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(
            new RuntConfirmationAttemptsQuery(runId, verdict, procedureTypeCode, procedureInstanceId, search, from, to, page, pageSize), ct)
            .ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetAttemptAsync(
        Guid id,
        [FromServices] GetRuntConfirmationAttemptHandler handler,
        CancellationToken ct)
    {
        var detail = await handler.HandleAsync(id, ct).ConfigureAwait(false);
        return detail is null ? Results.NotFound() : Results.Ok(detail);
    }

    private static async Task<IResult> GetAttemptRawAsync(
        Guid id,
        [FromServices] GetRuntConfirmationAttemptHandler handler,
        CancellationToken ct)
    {
        var raw = await handler.GetRawAsync(id, ct).ConfigureAwait(false);
        if (raw is null)
            return Results.NotFound();

        // Se devuelve el JSON tal como se guardó (ya sin credenciales ni binarios). En traspaso van
        // las dos respuestas, rotuladas, para que el auditor vea el par completo.
        var (primary, seller) = raw.Value;
        var body = seller is null
            ? $$"""{"primary":{{primary ?? "null"}}}"""
            : $$"""{"primary":{{primary ?? "null"}},"seller":{{seller}}}""";
        return Results.Content(body, "application/json");
    }

    private static async Task<IResult> ListRunsAsync(
        [FromQuery] int page,
        [FromQuery] int pageSize,
        [FromServices] ListRuntConfirmationRunsHandler handler,
        CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(page, pageSize, ct).ConfigureAwait(false));

    private static async Task<IResult> GetLatestRunAsync(
        [FromServices] ListRuntConfirmationRunsHandler handler,
        CancellationToken ct)
    {
        var run = await handler.LatestAsync(ct).ConfigureAwait(false);
        return run is null ? Results.NoContent() : Results.Ok(run);
    }

    private static async Task<IResult> ConsultNowAsync(
        Guid id,
        ClaimsPrincipal user,
        [FromServices] ConsultNowHandler handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new ConsultNowCommand(id, ResolveUserId(user)), ct).ConfigureAwait(false);
        return result.Status switch
        {
            ConsultNowStatus.NotFound => Results.NotFound(new { error = "tramite_no_encontrado" }),
            ConsultNowStatus.Conflict => Results.Conflict(new { error = result.ConflictCode }),
            _ => Results.Ok(new { attempt = result.Attempt, run = result.Run }),
        };
    }

    private static async Task<IResult> GetSettingsAsync(
        [FromServices] GetRuntConfirmationSettingsHandler handler,
        CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(ct).ConfigureAwait(false));

    /// <summary>Cuerpo del PUT. Todos los campos son obligatorios: la configuración se guarda completa, no por parches.</summary>
    public sealed record PutSettingsRequest(
        bool Enabled,
        string RunAtLocal,
        string ProviderKey,
        int GraceDays,
        int DiscrepancyAfterRuns,
        int MaxAttempts);

    private static async Task<IResult> PutSettingsAsync(
        [FromBody] PutSettingsRequest? request,
        ClaimsPrincipal user,
        [FromServices] UpdateRuntConfirmationSettingsHandler handler,
        CancellationToken ct)
    {
        if (request is null)
            return Results.BadRequest(new { error = "body_requerido", errors = Array.Empty<object>() });

        var result = await handler.HandleAsync(
            new UpdateRuntConfirmationSettingsCommand(
                request.Enabled,
                request.RunAtLocal,
                request.ProviderKey,
                request.GraceDays,
                request.DiscrepancyAfterRuns,
                request.MaxAttempts,
                ResolveUserId(user)),
            ct).ConfigureAwait(false);

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

    internal static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
