using System.Security.Claims;
using Flit.Admin.Application.Plataforma.Notificaciones;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// SuperAdmin — Plataforma → Notificaciones (HU #11366/#11367/#11368, Feature #11349). Alcance:
/// grupo de rutas + buzón de pruebas (leer/actualizar) + listado de canales con remitente resuelto
/// + envío de prueba con límite de frecuencia persistido (HU #11368).
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

        group.MapGet("/canales", GetChannelsAsync)
            .WithName("AdminPlataformaNotificacionesGetCanales")
            .Produces<NotificationChannelsResponse>(StatusCodes.Status200OK);

        // HU #11368 — envío de prueba de una plantilla del catálogo al buzón de pruebas, con
        // límite de frecuencia persistido (AC2). Sub-recurso de /buzon-pruebas: usa SU
        // destinatario, nunca uno recibido en el cuerpo de esta petición.
        group.MapPost("/buzon-pruebas/envios", SendTestAsync)
            .WithName("AdminPlataformaNotificacionesEnviarPrueba")
            .Produces<NotificationTestSendResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status500InternalServerError);

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

    private static async Task<IResult> GetChannelsAsync(
        [FromServices] INotificationChannelsAdminService service,
        CancellationToken ct)
    {
        var channels = await service.GetAsync(ct).ConfigureAwait(false);
        return Results.Ok(new NotificationChannelsResponse(
            [.. channels.Select(ToResponse)]));
    }

    private static async Task<IResult> SendTestAsync(
        [FromBody] SendNotificationTestBodyRequest request,
        ClaimsPrincipal user,
        [FromServices] INotificationTestSendAdminService service,
        CancellationToken ct)
    {
        var result = await service
            .SendAsync(
                new SendNotificationTestRequest(request.TemplateId, request.Channel),
                ResolveUserId(user),
                ct)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            NotificationTestSendOutcome.Sent => Results.Ok(ToResponse(result)),
            // AC8 — un fallo de TRANSPORTE (SMTP real que rechazó el mensaje) también se sella y se
            // reporta como resultado del intento, no como error HTTP: la solicitud en sí fue válida.
            NotificationTestSendOutcome.TransportFailed => Results.Ok(ToResponse(result)),
            NotificationTestSendOutcome.TemplateNotFound => Results.NotFound(
                new { error = "plantilla_no_encontrada", message = result.Message }),
            NotificationTestSendOutcome.InvalidChannel => Results.BadRequest(
                new { error = "canal_invalido", message = result.Message }),
            NotificationTestSendOutcome.MailboxNotConfigured => Results.BadRequest(
                new { error = "buzon_no_configurado", message = result.Message }),
            NotificationTestSendOutcome.ChannelNotConfigured => Results.BadRequest(
                new { error = "configuracion_incompleta", message = result.Message }),
            NotificationTestSendOutcome.RateLimited => Results.Json(
                new { error = "limite_frecuencia", message = result.Message, retryAfterSeconds = result.RetryAfterSeconds },
                statusCode: StatusCodes.Status429TooManyRequests),
            NotificationTestSendOutcome.RenderFailed => Results.Json(
                new { error = "fallo_render", message = result.Message },
                statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Json(
                new { error = "error_desconocido", message = result.Message },
                statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    private static NotificationTestSendResponse ToResponse(NotificationTestSendResult result) =>
        new(
            result.Success,
            result.Outcome.ToString(),
            result.Message,
            result.TemplateId,
            result.Channel,
            result.SenderEmail,
            result.SenderName,
            result.SentAt,
            result.IsConsoleTransport);

    private static NotificationChannelResponseItem ToResponse(NotificationChannelView view) =>
        new(view.Channel, view.Label, view.IsDefault, view.IsConfigured, view.SenderEmail, view.SenderName);

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

/// <summary>Respuesta de <c>GET /api/v1/admin/plataforma/notificaciones/canales</c> (HU #11367).</summary>
public sealed record NotificationChannelsResponse(IReadOnlyList<NotificationChannelResponseItem> Channels);

/// <summary>Un canal de notificación con su remitente resuelto por configuración (HU #11367).</summary>
public sealed record NotificationChannelResponseItem(
    string Channel,
    string Label,
    bool IsDefault,
    bool IsConfigured,
    string? SenderEmail,
    string? SenderName);

/// <summary>Respuesta del buzón de pruebas (AC2/AC3).</summary>
public sealed record NotificationTestMailboxResponse(
    bool IsConfigured,
    string? TestRecipientEmail,
    DateTimeOffset? LastTestSentAt,
    long RowVersion);

/// <summary>
/// Cuerpo de <c>POST /api/v1/admin/plataforma/notificaciones/buzon-pruebas/envios</c> (HU #11368).
/// El destinatario NUNCA viaja en el cuerpo: siempre es el buzón de pruebas configurado.
/// </summary>
public sealed record SendNotificationTestBodyRequest(string? TemplateId, string? Channel);

/// <summary>
/// Respuesta de <c>POST /api/v1/admin/plataforma/notificaciones/buzon-pruebas/envios</c> (HU #11368,
/// AC1/AC8). <c>Outcome</c> es el literal del enum <c>NotificationTestSendOutcome</c>
/// (<c>"Sent"</c> | <c>"TransportFailed"</c> en un 200; ver el cuerpo de error para las demás
/// causas). <c>IsConsoleTransport</c> en <c>true</c> significa que NO salió un correo real (AC8).
/// </summary>
public sealed record NotificationTestSendResponse(
    bool Success,
    string Outcome,
    string Message,
    string? TemplateId,
    string? Channel,
    string? SenderEmail,
    string? SenderName,
    DateTimeOffset? SentAt,
    bool IsConsoleTransport);
