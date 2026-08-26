using Flit.Ict.Api.Authorization;
using Flit.Ict.Application.Clients;

namespace Flit.Ict.Api.Endpoints;

/// <summary>
/// Administración (CRUD) de los clientes de integración ICT (<c>/api/v1/ict/clients</c>), para el
/// submódulo "Clientes ICT" del frontend (dentro de Usuarios y Roles). El Gateway aplica JwtRequired
/// (JWT de plataforma); aquí se exige el permiso <c>ict.clients.manage</c> (SuperAdmin lo bypassa por rol).
/// El SuperAdmin administra clientes de cualquier compañía; un admin no-super, solo los de la suya.
/// El secreto del cliente lo genera el sistema y se devuelve UNA sola vez (nunca se persiste en claro).
/// </summary>
public static class IctClientAdminEndpoints
{
    public sealed record CreateClientRequest(string Username, Guid TenantId, string[]? Scopes, bool? TestMode);

    public sealed record UpdateClientRequest(bool? IsActive, bool? MustRotate, string[]? Scopes, bool? TestMode);

    public static IEndpointRouteBuilder MapIctClientAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/ict/clients");

        group.MapGet("/", async (
            HttpContext context,
            ListIntegrationClientsHandler handler,
            Guid? tenantId,
            CancellationToken ct) =>
        {
            var access = PlatformAccessReader.Read(context);
            if (!access.HasClientAdminAccess)
            {
                return Forbidden();
            }

            // SuperAdmin: filtra por el tenant pedido o los ve todos (null). Admin de compañía: solo el suyo.
            var scope = access.IsSuperAdmin ? tenantId : access.TenantId;
            var items = await handler.HandleAsync(scope, ct);
            return Results.Ok(new { items });
        });

        group.MapPost("/", async (
            HttpContext context,
            CreateClientRequest body,
            CreateIntegrationClientHandler handler,
            CancellationToken ct) =>
        {
            var access = PlatformAccessReader.Read(context);
            if (!access.HasClientAdminAccess)
            {
                return Forbidden();
            }

            // Admin no-super: solo puede crear en su propia compañía (ignora un tenant ajeno del payload).
            if (!access.IsSuperAdmin && access.TenantId is { } own && body.TenantId != own)
            {
                return Results.Json(new { error = "tenant_forbidden" }, statusCode: StatusCodes.Status403Forbidden);
            }

            var tenantId = access.IsSuperAdmin ? body.TenantId : (access.TenantId ?? body.TenantId);
            var (result, error) = await handler.HandleAsync(
                new CreateClientCommand(body.Username, tenantId, body.Scopes, body.TestMode ?? false), ct);
            return error is null ? Results.Ok(result) : MapError(error);
        });

        group.MapPatch("/{id:guid}", async (
            HttpContext context,
            Guid id,
            UpdateClientRequest body,
            UpdateIntegrationClientHandler handler,
            CancellationToken ct) =>
        {
            var access = PlatformAccessReader.Read(context);
            if (!access.HasClientAdminAccess)
            {
                return Forbidden();
            }

            var restrict = access.IsSuperAdmin ? (Guid?)null : access.TenantId;
            var (result, error) = await handler.HandleAsync(
                new UpdateClientCommand(id, body.IsActive, body.MustRotate, body.Scopes, body.TestMode), restrict, ct);
            return error is null ? Results.Ok(result) : MapError(error);
        });

        group.MapPost("/{id:guid}/reset-secret", async (
            HttpContext context,
            Guid id,
            ResetIntegrationClientSecretHandler handler,
            CancellationToken ct) =>
        {
            var access = PlatformAccessReader.Read(context);
            if (!access.HasClientAdminAccess)
            {
                return Forbidden();
            }

            var restrict = access.IsSuperAdmin ? (Guid?)null : access.TenantId;
            var (result, error) = await handler.HandleAsync(id, restrict, ct);
            return error is null ? Results.Ok(result) : MapError(error);
        });

        return app;
    }

    private static IResult Forbidden() =>
        Results.Json(new { error = "forbidden" }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult MapError(string error) => error switch
    {
        "not_found" => Results.Json(new { error }, statusCode: StatusCodes.Status404NotFound),
        "username_taken" => Results.Json(new { error }, statusCode: StatusCodes.Status409Conflict),
        "tenant_not_found" or "tenant_inactive" or "invalid_username" =>
            Results.Json(new { error }, statusCode: StatusCodes.Status422UnprocessableEntity),
        _ => Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest),
    };
}
