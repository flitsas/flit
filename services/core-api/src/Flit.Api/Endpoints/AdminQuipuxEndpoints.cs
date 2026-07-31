using System.Security.Claims;
using Flit.Api.Authorization;
using Flit.Modules.Quipux.Application.UseCases.Configuracion;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Configuración operativa de la integración Quipux (<c>admin.quipux_settings</c>, HU #10710).
/// Exclusivo SuperAdmin: el contrato con Quipux es de FLIT como consumidor (no de un tenant), y la
/// fila lleva credenciales (Quipux + AWS) que solo la plataforma administra.
/// </summary>
/// <remarks>
/// Los secretos (<c>password</c>, <c>awsSecretAccessKey</c>) se cifran con Data Protection en el
/// repositorio y NUNCA se devuelven: el <c>GET</c> solo informa si hay uno cargado
/// (<c>hasPassword</c>, <c>hasAwsSecretAccessKey</c>), y en el <c>PUT</c> reenviarlos vacíos
/// significa "no lo cambies", no "bórralo". Por eso la carga tiene que pasar por aquí y no por un
/// UPDATE en SQL: el worker no podría descifrar un secreto que no cifró la app.
/// </remarks>
public static class AdminQuipuxEndpoints
{
    public static IEndpointRouteBuilder MapAdminQuipuxEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/quipux")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Admin · Quipux");

        // GET /api/v1/admin/quipux/settings — configuración vigente, redactada. 204 si aún no hay fila.
        group.MapGet("/settings", GetSettingsAsync)
            .WithName("AdminQuipuxGetSettings")
            .WithSummary("Configuración vigente de la integración Quipux (sin secretos)")
            .Produces<QuipuxSettingsView>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // PUT /api/v1/admin/quipux/settings — alta o actualización de la fila única. Cifra los secretos.
        group.MapPut("/settings", SaveSettingsAsync)
            .WithName("AdminQuipuxSaveSettings")
            .WithSummary("Crea o actualiza la configuración Quipux (cifra los secretos; vacío = conservar)")
            .Produces<QuipuxSettingsView>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> GetSettingsAsync(
        [FromServices] ObtenerQuipuxSettingsHandler handler,
        CancellationToken cancellationToken)
    {
        var view = await handler.HandleAsync(cancellationToken).ConfigureAwait(false);
        return view is null ? Results.NoContent() : Results.Ok(view);
    }

    private static async Task<IResult> SaveSettingsAsync(
        GuardarQuipuxSettingsCommand command,
        ClaimsPrincipal user,
        [FromServices] GuardarQuipuxSettingsHandler handler,
        CancellationToken cancellationToken)
    {
        var view = await handler
            .HandleAsync(command, ResolveUserId(user), cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(view);
    }

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
