using System.Security.Claims;
using Flit.Admin.Application.Plataforma.Notificaciones;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// SuperAdmin — Plataforma → Notificaciones (HU #11366, Feature #11349). Alcance exacto de esta
/// HU: grupo de rutas + buzón de pruebas (leer/actualizar). El listado de canales es la HU #11367
/// y el envío de prueba la HU #11368 — ninguno de los dos vive aquí.
/// </summary>
/// <remarks>
/// AC5 — desviación deliberada respecto al hermano de Mandatos: ESTE módulo nunca escribe
/// política de tenant (nada de <c>{officeId}</c>/<c>{tenantId}</c>/<c>{otId}</c> en sus rutas).
/// El buzón de pruebas es global de plataforma — ver <c>NotificationTestSettingsRow</c>.
/// </remarks>
public static class AdminPlataformaNotificacionesEndpoints
{
    public static IEndpointRouteBuilder MapAdminPlataformaNotificacionesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/plataforma/notificaciones")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Admin · Plataforma · Notificaciones");

        group.MapGet("/buzon-pruebas", GetMailboxAsync)
            .WithName("AdminPlataformaNotificacionesGetBuzonPruebas")
            .Produces<NotificationTestMailboxResponse>(StatusCodes.Status200OK);

        group.MapPut("/buzon-pruebas", UpdateMailboxAsync)
            .WithName("AdminPlataformaNotificacionesUpdateBuzonPruebas")
            .Produces<NotificationTestMailboxResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> GetMailboxAsync(
        [FromServices] INotificationTestMailboxAdminService service,
        CancellationToken ct)
    {
        var view = await service.GetAsync(ct).ConfigureAwait(false);
        return Results.Ok(ToResponse(view));
    }

    private static async Task<IResult> UpdateMailboxAsync(
        [FromBody] UpdateNotificationTestMailboxBodyRequest request,
        ClaimsPrincipal user,
        [FromServices] INotificationTestMailboxAdminService service,
        CancellationToken ct)
    {
        var (status, view) = await service
            .UpdateRecipientAsync(
                new UpdateNotificationTestMailboxRequest(request.Email, request.RowVersion),
                ResolveUserId(user),
                ct)
            .ConfigureAwait(false);

        return status switch
        {
            NotificationTestMailboxWriteStatus.Ok => Results.Ok(ToResponse(view!)),
            NotificationTestMailboxWriteStatus.InvalidEmail =>
                Results.BadRequest(new { error = "correo_invalido" }),
            NotificationTestMailboxWriteStatus.Conflict =>
                Results.Conflict(new { error = "row_version_conflict" }),
            _ => Results.BadRequest(),
        };
    }

    private static NotificationTestMailboxResponse ToResponse(NotificationTestMailboxView view) =>
        new(view.IsConfigured, view.TestRecipientEmail, view.LastTestSentAt, view.RowVersion);

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}

/// <summary>Cuerpo de <c>PUT /api/v1/admin/plataforma/notificaciones/buzon-pruebas</c>.</summary>
public sealed record UpdateNotificationTestMailboxBodyRequest(string? Email, long? RowVersion);

/// <summary>Respuesta del buzón de pruebas (AC2/AC3).</summary>
public sealed record NotificationTestMailboxResponse(
    bool IsConfigured,
    string? TestRecipientEmail,
    DateTimeOffset? LastTestSentAt,
    long RowVersion);
