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

        // Multi-rol (HU #10506): el JWT emite un claim POR CADA rol activo, en orden no
        // determinístico — FindFirstValue solo evalúa el primero. Se evalúan TODOS los claims
        // de los tipos relevantes (fix post-review #10504).
        var roleValues = user.Claims
            .Where(c => c.Type == AdminAuthorization.RoleClaimType || c.Type == "role_code")
            .Select(c => c.Value);
        var isSuperAdmin = roleValues.Any(r => string.Equals(r, AdminAuthorization.SuperAdminRole, StringComparison.OrdinalIgnoreCase));

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
        // CF-02 (HU #10879) — consulta del paso 1 ANTES de crear el trámite: no lleva instancia en la
        // ruta, pero es tan tenant-scoped como el resto del runtime (usa los proveedores de consulta de
        // la compañía y busca duplicidad entre SUS trámites). Sin esta entrada el middleware no poblaba
        // http.Items y el endpoint respondía 403 "sin compañía asignada" a un usuario que sí la tiene.
        || path.Equals("/api/v1/tramites/preflight-preview", StringComparison.OrdinalIgnoreCase)
        // HU sin ADO 2026-08-11 — consulta RUES del paso 1 SIN trámite creado (casilla 19 del FUR):
        // mismo caso que preflight-preview justo arriba, sin instancia en la ruta pero tan
        // tenant-scoped como el resto (usa el proveedor RUES de la compañía). Sin esta entrada el
        // endpoint confiaría en el X-Tenant-Id crudo del cliente.
        || path.Equals("/api/v1/tramites/rues-preview", StringComparison.OrdinalIgnoreCase)
        // HU #10943 — StartsWithSegments, NO Equals: con la comparación exacta el listado y el create
        // quedaban scopeados, pero las rutas hijas (PATCH /{id} y POST /{id}/resend, edición y reenvío de
        // una prevalidación) caían fuera y el backend confiaba en el X-Tenant-Id crudo del cliente — un
        // caller podía editar el correo (PII) o gastar reenvíos de OTRA compañía. Mismo bug y mismo fix
        // que ya se aplicó a identity-validation más abajo.
        || path.StartsWithSegments("/api/v1/tramites/biometric-validations", StringComparison.OrdinalIgnoreCase)
        // Feature #10587 — placas disponibles para el wizard (Flujo A): el endpoint resuelve el tenant
        // desde http.Items (que puebla este middleware). Sin esto devolvía 403 al radicador de la compañía
        // aunque el JWT trae tenant_id (el middleware no lo scopeaba). Se impone el tenant desde el token.
        || path.StartsWithSegments("/api/v1/tramites/plate-preassign", StringComparison.OrdinalIgnoreCase)
        // Colas de dead-letter de validación de identidad (stuck/requeue): el tenant se impone desde el
        // JWT igual que el resto del runtime; sin esto el endpoint confiaba en el header crudo del cliente
        // y un company-user podía leer/reencolar las atascadas de otra compañía.
        || path.StartsWithSegments("/api/v1/tramites/identity-validation", StringComparison.OrdinalIgnoreCase)
        // HU #10903 — consumo del wizard (escrituras vigentes + lookup de representante por NIT): el
        // operador de la compañía solo lee SU tenant. El tenant se impone desde el JWT (no del header),
        // para que un company-user no consulte el directorio de otra compañía cambiando X-Tenant-Id.
        || path.StartsWithSegments("/api/v1/tramites/deeds", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/api/v1/tramites/legal-representatives", StringComparison.OrdinalIgnoreCase)
        // HU #10955 (AC5) — lookup de contacto de actores por documento: sin instancia en la ruta,
        // pero tan tenant-scoped como el resto del runtime (mismo bug de fondo que b68b71e3 si se
        // quedara fuera: el operador podría leer el contacto de una persona capturado por OTRA
        // compañía cambiando X-Tenant-Id). El tenant se impone desde el JWT, no del header crudo.
        || path.StartsWithSegments("/api/v1/tramites/actors", StringComparison.OrdinalIgnoreCase);

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
