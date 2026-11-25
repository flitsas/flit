using System.Security.Claims;
using Flit.Admin.Domain.Companies.Domains;
using Flit.Api.Authorization;
using Flit.Infrastructure.Persistence;
using Flit.Modules.Platform.Application.Apps;
using Flit.Modules.Platform.Application.Companies;
using Flit.Modules.Platform.Application.Hosts;
using Flit.Modules.Platform.Application.Manifest;
using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Modules.Platform.Domain.Companies;
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

        // GET /me/apps — productos que el usuario puede abrir, con su URL en este ambiente (o en los dominios de su red).
        group.MapGet("/me/apps", async (
            ClaimsPrincipal user,
            IDomainContextAccessor domain,
            ListMyAppsHandler handler,
            ITenantDomainRepository domains,
            CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var userId)
                || !RequestTenantResolver.TryResolveTenantId(user, out var tenantId))
            {
                return Results.Unauthorized();
            }

            var apps = await handler.HandleAsync(userId, tenantId, RequestTenantResolver.IsSuperAdmin(user), domain.Host, ct);
            if (domain is { Kind: DomainKind.Network, HeadTenantId: { } head })
                apps = NetworkHosts.Rebase(apps, await domains.ListActiveHostsByPurposeAsync(head, ct), domain.Host);
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

        // GET /admin/tenants/{tenantId}/products — estado de cada producto para una empresa, con quién lo cambió por
        // última vez y si todavía no está desplegado en este ambiente (pestaña «Productos» de la compañía).
        group.MapGet("/admin/tenants/{tenantId:guid}/products", async (
            Guid tenantId,
            IIdentityDb db,
            ListTenantProductsHandler handler,
            IProductHosts hosts,
            CancellationToken ct) =>
        {
            if (!await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId, ct))
                return Problem(StatusCodes.Status404NotFound, "TENANT_NOT_FOUND", "La empresa no existe.");

            var products = await handler.HandleAsync(tenantId, ct);
            var editors = products.Where(p => p.UpdatedBy is not null).Select(p => p.UpdatedBy!.Value).Distinct().ToList();
            var emails = await db.Users.AsNoTracking()
                .Where(u => editors.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Email, ct);

            return Results.Ok(products.Select(p => new
            {
                p.ProductCode,
                p.Name,
                p.Enabled,
                p.Notes,
                p.UpdatedAt,
                p.UpdatedBy,
                UpdatedByEmail = p.UpdatedBy is { } by && emails.TryGetValue(by, out var email) ? email : null,
                ComingSoon = hosts.IsComingSoon(p.ProductCode),
                p.ImpuestoApiKeyConfigurada,
                // HU #14694 (platform.v1 1.1.0): último estado reportado; null sin aprovisionamiento o sin reporte.
                Provisioning = p.Provisioning is { } estado
                    ? new
                    {
                        status = estado.Estado,
                        reason = estado.MotivoCodigo,
                        message = estado.Mensaje,
                        action = estado.Accion,
                        signatureConfirmed = estado.FirmaConfirmada,
                        updatedAt = estado.ActualizadoEn,
                    }
                    : null,
            }));
        }).RequireAuthorization(AdminAuthorization.SuperAdminPolicy).WithName("ListTenantProducts");

        // PUT /admin/tenants/{tenantId}/products/{productCode} — encender o apagar, idempotente y auditado.
        group.MapPut("/admin/tenants/{tenantId:guid}/products/{productCode}", async (
            Guid tenantId,
            string productCode,
            [FromBody] SetTenantProductRequest request,
            ClaimsPrincipal user,
            IIdentityDb db,
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
                    TenantProductException.ProvisioningPrerequisiteMissing => StatusCodes.Status409Conflict,
                    _ => StatusCodes.Status400BadRequest,
                };
                return Problem(status, ex.Code, ex.Message, TituloDe(ex.Code));
            }
        }).RequireAuthorization(AdminAuthorization.SuperAdminPolicy).WithName("SetTenantProduct");

        // HU #14694 (platform.v1 1.1.0, plan §1.3 P1) GET /admin/tenants/{tenantId}/products/{productCode}/alta-previa —
        // lo que el servicio del producto enviará al alta del proveedor (diálogo de activación). Solo lectura.
        group.MapGet("/admin/tenants/{tenantId:guid}/products/{productCode}/alta-previa", async (
            Guid tenantId,
            string productCode,
            ObtenerDatosAltaProveedorHandler handler,
            CancellationToken ct) =>
        {
            try
            {
                var datos = await handler.HandleAsync(tenantId, productCode, ct);
                if (datos is null)
                    return Problem(StatusCodes.Status404NotFound, "TENANT_NOT_FOUND", "La empresa no existe.");

                return Results.Ok(new
                {
                    legalName = datos.LegalName,
                    taxIdBase = datos.TaxIdBase,
                    nitStatus = NitEmpresa.Codigo(datos.EstadoNit),
                    contactEmail = datos.ContactEmail,
                    blocking = datos.CodigoBloqueo,
                });
            }
            catch (TenantProductException ex) when (ex.Code == TenantProductException.ProductWithoutProvisioning)
            {
                return Problem(StatusCodes.Status404NotFound, ex.Code, ex.Message);
            }
        }).RequireAuthorization(AdminAuthorization.SuperAdminPolicy).WithName("GetTenantProductProvisioningPreview");

        // HU #14694 (platform.v1 1.1.0, plan §1.2 R1) POST …/aprovisionamiento/reintentar — encola el trabajo
        // <producto>.aprovisionamiento.reintentar por la outbox con el id del SuperAdmin. Sin cuerpo; 202.
        group.MapPost("/admin/tenants/{tenantId:guid}/products/{productCode}/aprovisionamiento/reintentar", async (
            Guid tenantId,
            string productCode,
            ClaimsPrincipal user,
            IIdentityDb db,
            RetryProvisioningHandler handler,
            CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("sub"), out var solicitadoPor))
                return Results.Unauthorized();

            if (!await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId, ct))
                return Problem(StatusCodes.Status404NotFound, "TENANT_NOT_FOUND", "La empresa no existe.");

            try
            {
                await handler.HandleAsync(new RetryProvisioningCommand(tenantId, productCode), solicitadoPor, ct);
                return Results.Accepted();
            }
            catch (TenantProductException ex)
            {
                var status = ex.Code == TenantProductException.ProductNotEnabled
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status404NotFound;
                return Problem(status, ex.Code, ex.Message);
            }
        }).RequireAuthorization(AdminAuthorization.SuperAdminPolicy).WithName("RetryTenantProductProvisioning");

        // PUT /admin/tenants/{tenantId}/products/{productCode}/credentials/{credentialCode} — guarda o reemplaza la API key (ADR-0092).
        group.MapPut("/admin/tenants/{tenantId:guid}/products/{productCode}/credentials/{credentialCode}", async (
            Guid tenantId,
            string productCode,
            string credentialCode,
            [FromBody] SetTenantProductCredentialRequest request,
            ClaimsPrincipal user,
            IIdentityDb db,
            SetTenantProductCredentialHandler handler,
            CancellationToken ct) =>
        {
            if (!await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId, ct))
                return Problem(StatusCodes.Status404NotFound, "TENANT_NOT_FOUND", "La empresa no existe.");

            try
            {
                Guid? changedBy = Guid.TryParse(user.FindFirstValue("sub"), out var sub) ? sub : null;
                await handler.HandleAsync(
                    new SetTenantProductCredentialCommand(tenantId, productCode, credentialCode, request.Value),
                    changedBy,
                    ct);
                return Results.NoContent();
            }
            catch (TenantProductException ex)
            {
                var status = ex.Code switch
                {
                    TenantProductException.CredentialNotSupported => StatusCodes.Status404NotFound,
                    TenantProductException.CredentialInvalid => StatusCodes.Status400BadRequest,
                    _ => StatusCodes.Status400BadRequest,
                };
                return Problem(status, ex.Code, ex.Message);
            }
        }).RequireAuthorization(AdminAuthorization.SuperAdminPolicy).WithName("SetTenantProductCredential");

        // DELETE — borra la API key; idempotente (204 también si no había fila).
        group.MapDelete("/admin/tenants/{tenantId:guid}/products/{productCode}/credentials/{credentialCode}", async (
            Guid tenantId,
            string productCode,
            string credentialCode,
            ClaimsPrincipal user,
            IIdentityDb db,
            DeleteTenantProductCredentialHandler handler,
            CancellationToken ct) =>
        {
            if (!await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId, ct))
                return Problem(StatusCodes.Status404NotFound, "TENANT_NOT_FOUND", "La empresa no existe.");

            try
            {
                Guid? changedBy = Guid.TryParse(user.FindFirstValue("sub"), out var sub) ? sub : null;
                await handler.HandleAsync(tenantId, productCode, credentialCode, changedBy, ct);
                return Results.NoContent();
            }
            catch (TenantProductException ex)
            {
                var status = ex.Code == TenantProductException.CredentialNotSupported
                    ? StatusCodes.Status404NotFound
                    : StatusCodes.Status400BadRequest;
                return Problem(status, ex.Code, ex.Message);
            }
        }).RequireAuthorization(AdminAuthorization.SuperAdminPolicy).WithName("DeleteTenantProductCredential");

        return app;
    }

    /// <summary><c>scope</c> puede venir como un claim con valores separados por espacio (RFC 8693) o repetido.</summary>
    internal static bool HasScope(ClaimsPrincipal user, string scope) =>
        user.FindAll("scope")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope, StringComparer.Ordinal);

    private static IResult Problem(int status, string code, string detail, string? title = null) =>
        Results.Problem(statusCode: status, detail: detail, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });

    /// <summary>Títulos cerrados en el contrato (platform.v1 1.1.0); los demás códigos usan el título por defecto del estado.</summary>
    private static string? TituloDe(string code) =>
        code == TenantProductException.ProvisioningPrerequisiteMissing ? "Falta un requisito para activar el producto" : null;

    internal sealed record SetTenantProductRequest(bool Enabled, string? Notes);

    internal sealed record SetTenantProductCredentialRequest(string? Value);

    internal sealed record ManifestRequest(string? Version, List<ManifestModuleRequest>? Modules, List<ManifestRoleRequest>? DefaultRoles);

    internal sealed record ManifestModuleRequest(string? Code, string? Name, List<ManifestPermissionRequest>? Permissions);

    internal sealed record ManifestPermissionRequest(string? Slug, string? Name);

    internal sealed record ManifestRoleRequest(string? Code, string? Name, List<string>? Permissions);
}
