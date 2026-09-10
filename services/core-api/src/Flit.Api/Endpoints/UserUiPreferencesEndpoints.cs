using System.Security.Claims;
using System.Text.Json;
using Flit.Modules.Security.Application.UiPreferences;
using Flit.Modules.Security.Application.UiPreferences.GetUserUiPreference;
using Flit.Modules.Security.Application.UiPreferences.UpsertUserUiPreference;
using Flit.Modules.Security.Domain.UiPreferences;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Preferencias de UI del usuario autenticado (base compartida de los criterios que permiten
/// elegir qué columnas ve en las tablas de trámites). El <c>user_id</c> SIEMPRE sale del claim
/// JWT (<c>sub</c>/NameIdentifier) — nunca del body — para que un usuario no pueda leer/escribir
/// la preferencia de otro con solo cambiar un id en la URL o el payload.
/// </summary>
public static class UserUiPreferencesEndpoints
{
    public static IEndpointRouteBuilder MapUserUiPreferencesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1/me/ui-preferences").RequireAuthorization();

        group.MapGet("/{scope}", GetAsync)
            .WithName("GetUserUiPreference")
            .WithSummary("Obtiene la preferencia de UI del usuario autenticado para un scope")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPut("/{scope}", UpsertAsync)
            .WithName("UpsertUserUiPreference")
            .WithSummary("Guarda (crea o reemplaza) la preferencia de UI del usuario autenticado para un scope")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> GetAsync(
        string scope,
        HttpContext httpContext,
        [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
        GetUserUiPreferenceHandler handler,
        CancellationToken cancellationToken)
    {
        if (tenantId is null || tenantId == Guid.Empty)
            return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

        var userId = ResolveUserId(httpContext.User);
        if (userId is null)
            return Results.Unauthorized();

        try
        {
            var result = await handler.HandleAsync(
                new GetUserUiPreferenceQuery { TenantId = tenantId.Value, UserId = userId.Value, Scope = scope },
                cancellationToken).ConfigureAwait(false);

            return Results.Ok(ToResponse(result));
        }
        catch (InvalidUiPreferenceScopeException ex)
        {
            return Results.Problem(statusCode: 400, title: "Bad Request", detail: ex.Message);
        }
    }

    private static async Task<IResult> UpsertAsync(
        string scope,
        HttpContext httpContext,
        [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
        [FromBody] UpsertUserUiPreferenceRequest? request,
        UpsertUserUiPreferenceHandler handler,
        CancellationToken cancellationToken)
    {
        if (tenantId is null || tenantId == Guid.Empty)
            return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

        var userId = ResolveUserId(httpContext.User);
        if (userId is null)
            return Results.Unauthorized();

        // value ausente en el body se trata como objeto vacío (mismo criterio que "sin fila
        // guardada" en el GET): guardar sin value no debería ser un 400, es simplemente vaciar.
        var valueJson = request?.Value is { ValueKind: not JsonValueKind.Undefined } value
            ? value.GetRawText()
            : "{}";

        try
        {
            var result = await handler.HandleAsync(
                new UpsertUserUiPreferenceCommand
                {
                    TenantId = tenantId.Value,
                    UserId = userId.Value,
                    Scope = scope,
                    ValueJson = valueJson,
                },
                cancellationToken).ConfigureAwait(false);

            return Results.Ok(ToResponse(result));
        }
        catch (InvalidUiPreferenceScopeException ex)
        {
            return Results.Problem(statusCode: 400, title: "Bad Request", detail: ex.Message);
        }
    }

    /// <summary>Id del usuario autenticado (claim <c>sub</c>/NameIdentifier), o null si no resuelve.</summary>
    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>
    /// Traduce el JSON crudo (opaco para Application) a un <see cref="JsonElement"/> real, para
    /// que <c>value</c> serialice como objeto JSON anidado y NO como una cadena con JSON escapado.
    /// </summary>
    private static object ToResponse(UserUiPreferenceResponse response) => new
    {
        scope = response.Scope,
        value = JsonDocument.Parse(response.ValueJson).RootElement.Clone(),
    };
}

/// <summary>Body de PUT /api/v1/me/ui-preferences/{scope}: <c>{ "value": { ... } }</c>.</summary>
public sealed class UpsertUserUiPreferenceRequest
{
    public JsonElement? Value { get; set; }
}
