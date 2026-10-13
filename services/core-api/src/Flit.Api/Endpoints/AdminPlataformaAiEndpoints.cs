using System.Security.Claims;
using Flit.Admin.Application.Plataforma.Ai;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// SuperAdmin — Plataforma → Configuración de IA (Feature #13508, HU #13509).
/// Prefijo <c>/api/v1/admin/plataforma/ai</c>; secretos solo en env, nunca en responses.
/// </summary>
public static class AdminPlataformaAiEndpoints
{
    public static IEndpointRouteBuilder MapAdminPlataformaAiEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/plataforma/ai")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Admin · Plataforma · IA");

        group.MapGet("/tools", ListToolsAsync)
            .WithName("AdminPlataformaAiListTools")
            .Produces<AiToolsListView>(StatusCodes.Status200OK);

        group.MapGet("/tools/{toolCode}", GetToolAsync)
            .WithName("AdminPlataformaAiGetTool")
            .Produces<AiToolSettingsView>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/tools/{toolCode}/settings", PutSettingsAsync)
            .WithName("AdminPlataformaAiPutToolSettings")
            .Produces<AiToolSettingsView>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/tools/{toolCode}/activate", ActivateAsync)
            .WithName("AdminPlataformaAiActivateProvider")
            .Produces<AiToolSettingsView>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    public sealed record PutAiToolSettingsBody(
        bool AutoReturnToPrimary,
        IReadOnlyList<AiProviderSlotBody> Providers);

    public sealed record AiProviderSlotBody(string ProviderCode, bool Enabled, int PriorityRank);

    public sealed record ActivateAiProviderBody(string ProviderCode, string? Reason);

    private static async Task<IResult> ListToolsAsync(
        [FromServices] ListAiToolsHandler handler,
        CancellationToken cancellationToken)
    {
        var view = await handler.HandleAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(view);
    }

    private static async Task<IResult> GetToolAsync(
        string toolCode,
        [FromServices] GetAiToolSettingsHandler handler,
        CancellationToken cancellationToken)
    {
        var view = await handler.HandleAsync(toolCode, cancellationToken).ConfigureAwait(false);
        return view is null ? Results.NotFound(new { error = "herramienta_desconocida" }) : Results.Ok(view);
    }

    private static async Task<IResult> PutSettingsAsync(
        string toolCode,
        [FromBody] PutAiToolSettingsBody? body,
        ClaimsPrincipal user,
        [FromServices] SaveAiToolSettingsHandler handler,
        CancellationToken cancellationToken)
    {
        if (body is null || body.Providers is null || body.Providers.Count == 0)
        {
            return Results.BadRequest(new { error = "configuracion_invalida" });
        }

        var command = new SaveAiToolSettingsCommand(
            body.AutoReturnToPrimary,
            body.Providers.Select(p => new AiProviderSlotCommand(p.ProviderCode, p.Enabled, p.PriorityRank)).ToList(),
            ResolveUserId(user));

        var result = await handler.HandleAsync(toolCode, command, cancellationToken).ConfigureAwait(false);

        if (!result.IsValid)
        {
            return result.ErrorCode switch
            {
                "herramienta_desconocida" or "herramienta_sin_configuracion" =>
                    Results.NotFound(new { error = result.ErrorCode }),
                _ => Results.BadRequest(new { error = result.ErrorCode ?? "configuracion_invalida" }),
            };
        }

        return Results.Ok(result.View);
    }

    private static async Task<IResult> ActivateAsync(
        string toolCode,
        [FromBody] ActivateAiProviderBody? body,
        ClaimsPrincipal user,
        [FromServices] ActivateAiProviderHandler handler,
        CancellationToken cancellationToken)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.ProviderCode))
        {
            return Results.BadRequest(new { error = "proveedor_invalido" });
        }

        var result = await handler
            .HandleAsync(
                toolCode,
                new ActivateAiProviderCommand(body.ProviderCode, body.Reason, ResolveUserId(user)),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            ActivateAiProviderOutcome.Ok or ActivateAiProviderOutcome.Unchanged => Results.Ok(result.View),
            ActivateAiProviderOutcome.ToolNotFound => Results.NotFound(new { error = "herramienta_desconocida" }),
            ActivateAiProviderOutcome.UnknownProvider => Results.BadRequest(new { error = "proveedor_desconocido" }),
            ActivateAiProviderOutcome.ProviderDisabled => Results.BadRequest(new { error = "proveedor_deshabilitado" }),
            ActivateAiProviderOutcome.ProviderNotCapable => Results.BadRequest(new { error = "proveedor_no_aplica" }),
            ActivateAiProviderOutcome.ProviderNoApiKey => Results.UnprocessableEntity(new { error = "proveedor_sin_credencial" }),
            _ => Results.BadRequest(),
        };
    }

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
