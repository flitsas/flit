using System.Security.Claims;
using Flit.Admin.Application.OtProfile.GetOtProfile;
using Flit.Admin.Application.OtProfile.UpdateOtFeatureFlag;
using Flit.Admin.Application.OtProfile.UpdateOtProfile;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Endpoints de administración OT — perfil, modo Dashboard/QX y feature flags (HU #10215).
/// El tenant se resuelve exclusivamente del claim JWT <c>tenant_id</c> (AC5).
/// </summary>
public static class AdminOtEndpoints
{
    public static IEndpointRouteBuilder MapAdminOtEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/ot")
            .RequireAuthorization(AdminAuthorization.OtAdminPolicy)
            .WithTags("Admin · OT");

        group.MapGet("/profile", GetProfileAsync)
            .WithName("AdminOtGetProfile")
            .WithSummary("Obtiene el perfil OT del tenant autenticado")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPatch("/profile", UpdateProfileAsync)
            .WithName("AdminOtUpdateProfile")
            .WithSummary("Actualiza el perfil OT del tenant autenticado")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapPatch("/feature-flags/{id:guid}", UpdateFeatureFlagAsync)
            .WithName("AdminOtUpdateFeatureFlag")
            .WithSummary("Activa o desactiva un feature flag OT")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> GetProfileAsync(
        HttpContext httpContext,
        GetOtProfileHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Results.Json(
                new { error = "Token inválido: falta claim tenant_id" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var response = await handler.HandleAsync(
            new GetOtProfileQuery { TenantId = tenantId },
            cancellationToken).ConfigureAwait(false);

        return Results.Ok(response);
    }

    private static async Task<IResult> UpdateProfileAsync(
        HttpContext httpContext,
        UpdateOtProfileRequest request,
        UpdateOtProfileHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Results.Json(
                new { error = "Token inválido: falta claim tenant_id" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await handler.HandleAsync(new UpdateOtProfileCommand
        {
            TenantId = tenantId,
            ChangedBy = ResolveUserId(httpContext.User),
            Request = request,
        }, cancellationToken).ConfigureAwait(false);

        if (!result.IsValid)
        {
            return Results.Json(
                new { errors = result.Errors.Select(e => new { field = e.Field, message = e.Message }) },
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        return Results.Ok(result.Profile);
    }

    private static async Task<IResult> UpdateFeatureFlagAsync(
        Guid id,
        HttpContext httpContext,
        UpdateOtFeatureFlagRequest request,
        UpdateOtFeatureFlagHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Results.Json(
                new { error = "Token inválido: falta claim tenant_id" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await handler.HandleAsync(new UpdateOtFeatureFlagCommand
        {
            TenantId = tenantId,
            FlagId = id,
            ChangedBy = ResolveUserId(httpContext.User),
            Request = request,
        }, cancellationToken).ConfigureAwait(false);

        return result.Status switch
        {
            UpdateOtFeatureFlagStatus.NotFound => Results.NotFound(new { error = "Feature flag no encontrado" }),
            _ => Results.Ok(result.Flag),
        };
    }

    private static bool TryResolveTenantId(ClaimsPrincipal user, out Guid tenantId)
    {
        var claim = user.FindFirstValue(AdminAuthorization.TenantIdClaimType);
        return Guid.TryParse(claim, out tenantId);
    }

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue("sub");
        return Guid.TryParse(sub, out var userId) ? userId : null;
    }
}
