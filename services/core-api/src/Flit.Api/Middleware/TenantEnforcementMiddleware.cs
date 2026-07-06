using System.Security.Claims;
using System.Text.Json;
using Flit.Api.Authorization;

namespace Flit.Api.Middleware;

/// <summary>
/// Enforcement multi-tenant para los endpoints RUNTIME de trámites
/// (<c>/api/v1/tramites/instances*</c>, <c>/transit-offices</c>, <c>/biometric-validations</c>,
/// <c>/identity-validation/*</c>).
/// El tenant se resuelve desde el JWT, NO del header que mande el cliente:
/// <list type="bullet">
///   <item>No autenticado → 401.</item>
///   <item>Usuario de compañía (rol ≠ SuperAdmin): se SOBRESCRIBE <c>X-Tenant-Id</c> con el
///   <c>tenant_id</c> del token (no puede leer otra empresa cambiando el header). Sin
///   <c>tenant_id</c> en el token → 403.</item>
///   <item>SuperAdmin: acceso multi-tenant. Respeta el <c>X-Tenant-Id</c> que mande (para acotar
///   a una empresa) o su ausencia (ver todo en el listado).</item>
/// </list>
/// El tenant resuelto (o <c>null</c> = todos, solo superadmin) y <c>isSuperAdmin</c> quedan en
/// <see cref="HttpContext.Items"/> para el create (que toma el tenant del body) y otros consumidores.
/// NO aplica a la parametrización (<c>/api/v1/tramites/procedure-types</c>, etc.), que usa la
/// policy <c>SuperAdmin</c> unificada por rol JWT (HU #10508), ni al portal público
/// (<c>/api/v1/public/*</c>).
/// </summary>
public sealed class TenantEnforcementMiddleware(RequestDelegate next)
{
    /// <summary>Clave en <see cref="HttpContext.Items"/> del tenant resuelto (Guid? — null = todos).</summary>
    public const string TenantItemKey = "tramites.tenantId";

    /// <summary>Clave en <see cref="HttpContext.Items"/> de si el caller es SuperAdmin (bool).</summary>
    public const string SuperAdminItemKey = "tramites.isSuperAdmin";

    private const string TenantHeader = "X-Tenant-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsRuntimeScoped(context.Request.Path))
        {
            await next(context);
            return;
        }

        var user = context.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            await WriteProblemAsync(context, StatusCodes.Status401Unauthorized, "Unauthorized",
                "Se requiere autenticación para acceder a los trámites.");
            return;
        }

        var role = user.FindFirstValue(AdminAuthorization.RoleClaimType)
            ?? user.FindFirstValue("role_code")
            ?? string.Empty;
        var isSuperAdmin = string.Equals(role, AdminAuthorization.SuperAdminRole, StringComparison.OrdinalIgnoreCase);

        if (isSuperAdmin)
        {
            // SuperAdmin: respeta el tenant del header si lo manda (acota a una empresa); si no, null = todos.
            context.Items[SuperAdminItemKey] = true;
            context.Items[TenantItemKey] = TryReadHeaderTenant(context, out var selected) ? selected : (Guid?)null;
            await next(context);
            return;
        }

        // Usuario de compañía: el tenant SALE del token; se ignora/sobreescribe el header del cliente.
        if (!Guid.TryParse(user.FindFirstValue(AdminAuthorization.TenantIdClaimType), out var tenantId)
            || tenantId == Guid.Empty)
        {
            await WriteProblemAsync(context, StatusCodes.Status403Forbidden, "Forbidden",
                "El usuario autenticado no tiene una compañía asignada.");
            return;
        }

        context.Request.Headers[TenantHeader] = tenantId.ToString();
        context.Items[SuperAdminItemKey] = false;
        context.Items[TenantItemKey] = tenantId;
        await next(context);
    }

    /// <summary>Endpoints runtime tenant-scoped (excluye parametrización y portal público).</summary>
    private static bool IsRuntimeScoped(PathString path) =>
        path.StartsWithSegments("/api/v1/tramites/instances", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/api/v1/tramites/transit-offices", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/api/v1/tramites/biometric-validations", StringComparison.OrdinalIgnoreCase)
        // Colas de dead-letter de validación de identidad (stuck/requeue): el tenant se impone desde el
        // JWT igual que el resto del runtime; sin esto el endpoint confiaba en el header crudo del cliente
        // y un company-user podía leer/reencolar las atascadas de otra compañía.
        || path.StartsWithSegments("/api/v1/tramites/identity-validation", StringComparison.OrdinalIgnoreCase);

    private static bool TryReadHeaderTenant(HttpContext context, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        var raw = context.Request.Headers[TenantHeader].ToString();
        return Guid.TryParse(raw, out tenantId) && tenantId != Guid.Empty;
    }

    private static Task WriteProblemAsync(HttpContext context, int status, string title, string detail)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            type = "about:blank",
            title,
            status,
            detail,
        }));
    }
}
