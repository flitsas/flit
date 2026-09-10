using System.Security.Claims;
using Flit.Api.Authorization;
using Flit.Tramites.Application.UseCases.RuntConfirmation;
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

        return app;
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
