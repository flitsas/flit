using System.Security.Claims;
using Flit.Admin.Application.Plataforma.DrFlit;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// SuperAdmin — Plataforma → Asistente DR. FLIT (HU #14771, Feature #14769).
/// GET/PUT de la visibilidad UI (fila única <c>admin.dr_flit_settings</c>).
/// </summary>
public static class AdminPlataformaDrFlitEndpoints
{
    public static IEndpointRouteBuilder MapAdminPlataformaDrFlitEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/plataforma/dr-flit")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Admin · Plataforma · DR. FLIT");

        group.MapGet("/", GetAsync)
            .WithName("AdminPlataformaDrFlitGetSettings")
            .WithSummary("Lee la visibilidad de la UI de DR. FLIT")
            .Produces<DrFlitPlatformSettingsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPut("/", UpdateAsync)
            .WithName("AdminPlataformaDrFlitUpdateSettings")
            .WithSummary("Actualiza la visibilidad de la UI de DR. FLIT")
            .Produces<DrFlitPlatformSettingsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> GetAsync(
        [FromServices] IDrFlitPlatformSettingsAdminService service,
        CancellationToken ct)
    {
        var view = await service.GetAsync(ct).ConfigureAwait(false);
        return Results.Ok(ToResponse(view));
    }

    private static async Task<IResult> UpdateAsync(
        [FromBody] UpdateDrFlitPlatformSettingsBodyRequest request,
        ClaimsPrincipal user,
        [FromServices] IDrFlitPlatformSettingsAdminService service,
        CancellationToken ct)
    {
        var (status, view) = await service
            .UpdateAsync(
                new UpdateDrFlitPlatformSettingsRequest(request.UiEnabled, request.RowVersion),
                ResolveUserId(user),
                ct)
            .ConfigureAwait(false);

        return status switch
        {
            DrFlitPlatformSettingsWriteStatus.Ok => Results.Ok(ToResponse(view!)),
            DrFlitPlatformSettingsWriteStatus.Conflict =>
                Results.Conflict(new { error = "row_version_conflict" }),
            _ => Results.BadRequest(),
        };
    }

    private static DrFlitPlatformSettingsResponse ToResponse(DrFlitPlatformSettingsView view) =>
        new(view.UiEnabled, view.RowVersion, view.UpdatedAt, view.UpdatedBy);

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}

/// <summary>Respuesta GET/PUT <c>/api/v1/admin/plataforma/dr-flit</c>.</summary>
public sealed record DrFlitPlatformSettingsResponse(
    bool UiEnabled,
    long RowVersion,
    DateTimeOffset? UpdatedAt,
    Guid? UpdatedBy);

/// <summary>Cuerpo PUT <c>/api/v1/admin/plataforma/dr-flit</c>.</summary>
public sealed record UpdateDrFlitPlatformSettingsBodyRequest(bool UiEnabled, long? RowVersion);
