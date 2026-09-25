using System.Security.Claims;
using Flit.Api.Authorization;
using Flit.Infrastructure.Persistence;
using Flit.Modules.Platform.Application.Apps;
using Flit.Modules.Platform.Application.Manifest;
using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Modules.Platform.Domain.Manifest;
using Flit.Modules.Security.Application.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Flit.Api.Endpoints.Platform;

/// <summary>
/// Endpoints de plataforma de la FLIT Suite (contrato v1 §6; HU #12966, B-06), bajo <c>/api/v1/platform</c>.
/// Documentados en <c>contracts/openapi/platform.v1.yaml</c>. Los errores son RFC 7807 con <c>code</c> (§10).
/// </summary>
public static class PlatformEndpoints
{
    /// <summary>Scope del token de servicio que puede registrar un manifiesto (contrato §3).</summary>
    public const string ManifestScope = "platform.manifest";

    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/platform").WithTags("Platform");

        // GET /me/apps — productos que el usuario puede abrir, con su URL en este ambiente.
        group.MapGet("/me/apps", async (
            ClaimsPrincipal user,
            IDomainContextAccessor domain,
            ListMyAppsHandler handler,
            CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var userId)
                || !RequestTenantResolver.TryResolveTenantId(user, out var tenantId))
            {
                return Results.Unauthorized();
            }

            var apps = await handler.HandleAsync(userId, tenantId, RequestTenantResolver.IsSuperAdmin(user), domain.Host, ct);
            return Results.Ok(apps);
        }).RequireAuthorization().WithName("ListMyApps");

        // PUT /products/{code}/manifest — lo llama cada servicio al arrancar, con token de servicio.
        group.MapPut("/products/{code}/manifest", async (
            string code,
            ManifestRequest request,
            ApplyProductManifestHandler handler,
            CancellationToken ct) =>
        {
            try
            {
                var manifest = new ProductManifest(
                    code,
                    request.Version ?? string.Empty,
                    (request.Modules ?? []).Select(m => new ManifestModule(
                        m.Code ?? string.Empty,
                        m.Name ?? string.Empty,
                        (m.Permissions ?? []).Select(p => new ManifestPermissionSlug(p.Slug ?? string.Empty, p.Name ?? string.Empty)).ToList())).ToList(),
                    (request.DefaultRoles ?? []).Select(r => new ManifestRole(
                        r.Code ?? string.Empty,
                        r.Name ?? string.Empty,
                        r.Permissions ?? [])).ToList());
                var result = await handler.HandleAsync(manifest, ct);
                return Results.Ok(result);
            }
            catch (ManifestValidationException ex)
            {
                return Problem(ex.Code == "PRODUCT_NOT_FOUND" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest, ex.Code, ex.Message);
            }
            catch (ManifestConflictException ex)
            {
                return Problem(StatusCodes.Status409Conflict, "MANIFEST_CONFLICT", ex.Message);
            }
        }).RequireAuthorization(p => p.RequireAssertion(ctx => HasScope(ctx.User, ManifestScope)))
          .WithName("ApplyProductManifest");

        // GET /admin/tenants/{tenantId}/products — estado de cada producto para una empresa.
        group.MapGet("/admin/tenants/{tenantId:guid}/products", async (
            Guid tenantId,
            FlitDbContext db,
            ListTenantProductsHandler handler,
            CancellationToken ct) =>
        {
            if (!await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId, ct))
                return Problem(StatusCodes.Status404NotFound, "TENANT_NOT_FOUND", "La empresa no existe.");

            return Results.Ok(await handler.HandleAsync(tenantId, ct));
        }).RequireAuthorization(AdminAuthorization.SuperAdminPolicy).WithName("ListTenantProducts");

        // PUT /admin/tenants/{tenantId}/products/{productCode} — encender o apagar, idempotente y auditado.
        group.MapPut("/admin/tenants/{tenantId:guid}/products/{productCode}", async (
            Guid tenantId,
            string productCode,
            [FromBody] SetTenantProductRequest request,
            ClaimsPrincipal user,
            FlitDbContext db,
            SetTenantProductEnabledHandler handler,
            CancellationToken ct) =>
        {
            if (!await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId, ct))
                return Problem(StatusCodes.Status404NotFound, "TENANT_NOT_FOUND", "La empresa no existe.");

            try
            {
                Guid? changedBy = Guid.TryParse(user.FindFirstValue("sub"), out var sub) ? sub : null;
                var change = await handler.HandleAsync(new SetTenantProductEnabledCommand(tenantId, productCode, request.Enabled, request.Notes), changedBy, ct);
                return Results.Ok(new
                {
                    productCode = change.Current.ProductCode,
                    enabled = change.Current.Enabled,
                    notes = change.Current.Notes,
                    updatedAt = change.Current.UpdatedAt,
                    updatedBy = change.Current.UpdatedBy,
                    changed = change.Changed,
                });
            }
            catch (TenantProductException ex)
            {
                var status = ex.Code switch
                {
                    TenantProductException.ProductNotFound => StatusCodes.Status404NotFound,
                    TenantProductException.ProductInactive or TenantProductException.ProductNotEnabledForHead => StatusCodes.Status409Conflict,
                    _ => StatusCodes.Status400BadRequest,
                };
                return Problem(status, ex.Code, ex.Message);
            }
        }).RequireAuthorization(AdminAuthorization.SuperAdminPolicy).WithName("SetTenantProduct");

        return app;
    }

    /// <summary><c>scope</c> puede venir como un claim con valores separados por espacio (RFC 8693) o repetido.</summary>
    internal static bool HasScope(ClaimsPrincipal user, string scope) =>
        user.FindAll("scope")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope, StringComparer.Ordinal);

    private static IResult Problem(int status, string code, string detail) =>
        Results.Problem(statusCode: status, detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });

    internal sealed record SetTenantProductRequest(bool Enabled, string? Notes);

    internal sealed record ManifestRequest(string? Version, List<ManifestModuleRequest>? Modules, List<ManifestRoleRequest>? DefaultRoles);

    internal sealed record ManifestModuleRequest(string? Code, string? Name, List<ManifestPermissionRequest>? Permissions);

    internal sealed record ManifestPermissionRequest(string? Slug, string? Name);

    internal sealed record ManifestRoleRequest(string? Code, string? Name, List<string>? Permissions);
}
