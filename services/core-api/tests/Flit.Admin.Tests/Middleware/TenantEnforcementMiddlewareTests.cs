using System.Security.Claims;
using Flit.Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Flit.Admin.Tests.Middleware;

/// <summary>
/// Enforcement multi-tenant de los endpoints runtime de trámites (#1). El tenant se resuelve del
/// JWT, NO del header del cliente: company-user queda atado a su compañía; superadmin es multi-tenant.
/// </summary>
public sealed class TenantEnforcementMiddlewareTests
{
    private const string CompanyTenant = "11111111-1111-1111-1111-111111111111";
    private const string OtherTenant = "99999999-9999-9999-9999-999999999999";

    private static DefaultHttpContext Context(string path, ClaimsPrincipal? user = null, string? headerTenant = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Response.Body = new MemoryStream();
        if (user is not null)
            ctx.User = user;
        if (headerTenant is not null)
            ctx.Request.Headers["X-Tenant-Id"] = headerTenant;
        return ctx;
    }

    private static ClaimsPrincipal User(string role, string? tenantId)
    {
        var claims = new List<Claim> { new("role", role) };
        if (tenantId is not null)
            claims.Add(new Claim("tenant_id", tenantId));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
    }

    private static async Task<bool> InvokeAsync(DefaultHttpContext ctx)
    {
        var nextCalled = false;
        var mw = new TenantEnforcementMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        await mw.InvokeAsync(ctx);
        return nextCalled;
    }

    [Fact]
    public async Task PathNoRuntime_PasaSinTocar()
    {
        // La parametrización (/procedure-types) usa otro modelo de auth → el middleware no aplica.
        var ctx = Context("/api/v1/tramites/procedure-types");
        var next = await InvokeAsync(ctx);

        next.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task NoAutenticado_Returns401()
    {
        var ctx = Context("/api/v1/tramites/instances");
        var next = await InvokeAsync(ctx);

        next.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task CompanyUser_SobreescribeHeaderConTenantDelToken()
    {
        // Aunque el cliente mande OTRO tenant en el header, el middleware lo fuerza al del JWT.
        var ctx = Context("/api/v1/tramites/instances",
            User("AdminCompany", CompanyTenant), headerTenant: OtherTenant);
        var next = await InvokeAsync(ctx);

        next.Should().BeTrue();
        ctx.Request.Headers["X-Tenant-Id"].ToString().Should().Be(CompanyTenant);
        ctx.Items[TenantEnforcementMiddleware.TenantItemKey].Should().Be(Guid.Parse(CompanyTenant));
        ctx.Items[TenantEnforcementMiddleware.SuperAdminItemKey].Should().Be(false);
    }

    [Fact]
    public async Task CompanyUser_SinTenantEnToken_Returns403()
    {
        var ctx = Context("/api/v1/tramites/instances", User("AdminCompany", tenantId: null));
        var next = await InvokeAsync(ctx);

        next.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task SuperAdmin_SinHeader_TenantNullYVeTodo()
    {
        var ctx = Context("/api/v1/tramites/instances", User("SuperAdmin", tenantId: null));
        var next = await InvokeAsync(ctx);

        next.Should().BeTrue();
        ctx.Items[TenantEnforcementMiddleware.SuperAdminItemKey].Should().Be(true);
        ctx.Items[TenantEnforcementMiddleware.TenantItemKey].Should().BeNull();
    }

    // Fix post-review #10504 — multi-rol (HU #10506): el JWT emite un claim POR CADA rol
    // activo, en orden no determinístico. FindFirstValue (usado antes del fix) solo evalúa el
    // PRIMERO, así que un SuperAdmin con otro rol emitido antes perdía el bypass multi-tenant.
    [Fact]
    public async Task SuperAdmin_MultiRoleNotFirstClaim_SigueSiendoReconocido()
    {
        var claims = new List<Claim>
        {
            new("role", "Radicador"),
            new("role", "SuperAdmin"),
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));

        var ctx = Context("/api/v1/tramites/instances", user);
        var next = await InvokeAsync(ctx);

        next.Should().BeTrue();
        ctx.Items[TenantEnforcementMiddleware.SuperAdminItemKey].Should().Be(true,
            "deben evaluarse TODOS los claims 'role', no solo el primero");
        ctx.Items[TenantEnforcementMiddleware.TenantItemKey].Should().BeNull();
    }

    [Fact]
    public async Task SuperAdmin_ConHeader_AcotaAEsaCompania()
    {
        var ctx = Context("/api/v1/tramites/instances",
            User("SuperAdmin", tenantId: CompanyTenant), headerTenant: OtherTenant);
        var next = await InvokeAsync(ctx);

        next.Should().BeTrue();
        // El superadmin SÍ respeta el header (acota a una compañía que elige), no su propio tenant.
        ctx.Items[TenantEnforcementMiddleware.TenantItemKey].Should().Be(Guid.Parse(OtherTenant));
    }

    [Fact]
    public async Task IdentityValidationStuck_CompanyUser_SobreescribeHeaderConTenantDelToken()
    {
        // Las colas de atascadas (stuck/requeue) son runtime tenant-scoped: aunque el cliente mande OTRO
        // tenant, el middleware lo fuerza al del JWT → un company-user no ve atascadas de otra compañía.
        var ctx = Context("/api/v1/tramites/identity-validation/stuck",
            User("AdminCompany", CompanyTenant), headerTenant: OtherTenant);
        var next = await InvokeAsync(ctx);

        next.Should().BeTrue();
        ctx.Request.Headers["X-Tenant-Id"].ToString().Should().Be(CompanyTenant);
        ctx.Items[TenantEnforcementMiddleware.TenantItemKey].Should().Be(Guid.Parse(CompanyTenant));
    }

    [Fact]
    public async Task IdentityValidationRequeue_NoAutenticado_Returns401()
    {
        var ctx = Context("/api/v1/tramites/identity-validation/stuck/requeue-all");
        var next = await InvokeAsync(ctx);

        next.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(401);
    }

    // Feature #10587 (bug de validación manual) — el endpoint de placas disponibles del wizard (Flujo A)
    // es runtime tenant-scoped: el radicador de la compañía queda atado al tenant del token. Antes NO
    // estaba en IsRuntimeScoped → http.Items no traía el tenant → el endpoint daba 403 al radicador.
    [Fact]
    public async Task PlatePreassignAvailable_CompanyUser_ResuelveTenantDelToken()
    {
        var ctx = Context("/api/v1/tramites/plate-preassign/available",
            User("Radicador", CompanyTenant));
        var next = await InvokeAsync(ctx);

        next.Should().BeTrue();
        ctx.Items[TenantEnforcementMiddleware.TenantItemKey].Should().Be(Guid.Parse(CompanyTenant));
        ctx.Items[TenantEnforcementMiddleware.SuperAdminItemKey].Should().Be(false);
    }

    // CF-02 (HU #10879) — la consulta del paso 1 corre ANTES de crear el trámite, así que su ruta no
    // cuelga de /instances. Igual es tenant-scoped: sin estar en IsRuntimeScoped, http.Items no traía
    // el tenant y el endpoint respondía 403 "sin compañía asignada" a un radicador que sí la tiene.
    [Fact]
    public async Task PreflightPreview_CompanyUser_ResuelveTenantDelToken()
    {
        var ctx = Context("/api/v1/tramites/preflight-preview",
            User("Radicador", CompanyTenant), headerTenant: OtherTenant);
        var next = await InvokeAsync(ctx);

        next.Should().BeTrue();
        // El header del cliente NO manda: el tenant sale del token, igual que en el resto del runtime.
        ctx.Request.Headers["X-Tenant-Id"].ToString().Should().Be(CompanyTenant);
        ctx.Items[TenantEnforcementMiddleware.TenantItemKey].Should().Be(Guid.Parse(CompanyTenant));
        ctx.Items[TenantEnforcementMiddleware.SuperAdminItemKey].Should().Be(false);
    }

    [Fact]
    public async Task PreflightPreview_NoAutenticado_Returns401()
    {
        var ctx = Context("/api/v1/tramites/preflight-preview");
        var next = await InvokeAsync(ctx);

        next.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(401);
    }

    // HU sin ADO 2026-08-11 — consulta RUES del paso 1 SIN trámite creado (casilla 19 del FUR): mismo
    // caso que PreflightPreview arriba, sin instancia en la ruta.
    [Fact]
    public async Task RuesPreview_CompanyUser_ResuelveTenantDelToken()
    {
        var ctx = Context("/api/v1/tramites/rues-preview",
            User("Radicador", CompanyTenant), headerTenant: OtherTenant);
        var next = await InvokeAsync(ctx);

        next.Should().BeTrue();
        // El header del cliente NO manda: el tenant sale del token, igual que en el resto del runtime.
        ctx.Request.Headers["X-Tenant-Id"].ToString().Should().Be(CompanyTenant);
        ctx.Items[TenantEnforcementMiddleware.TenantItemKey].Should().Be(Guid.Parse(CompanyTenant));
        ctx.Items[TenantEnforcementMiddleware.SuperAdminItemKey].Should().Be(false);
    }

    [Fact]
    public async Task RuesPreview_NoAutenticado_Returns401()
    {
        var ctx = Context("/api/v1/tramites/rues-preview");
        var next = await InvokeAsync(ctx);

        next.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(401);
    }

    // HU #10943 (hallazgo de review, IDOR cross-tenant) — antes IsRuntimeScoped comparaba
    // "/api/v1/tramites/biometric-validations" con Equals EXACTO: el listado/create quedaban
    // scopeados, pero las rutas HIJAS (PATCH /{id} y POST /{id}/resend — editar/reenviar una
    // prevalidación con PII) caían FUERA del enforcement y el endpoint confiaba en el
    // X-Tenant-Id crudo del cliente. Con StartsWithSegments el tenant se impone SIEMPRE desde
    // el JWT, igual que en el resto del runtime.

    [Fact]
    public async Task BiometricValidationEdit_CompanyUser_SobreescribeHeaderConTenantDelToken()
    {
        var ctx = Context("/api/v1/tramites/biometric-validations/22222222-2222-2222-2222-222222222222",
            User("AdminCompany", CompanyTenant), headerTenant: OtherTenant);
        var next = await InvokeAsync(ctx);

        next.Should().BeTrue();
        // Antes del fix, esta ruta NO entraba en IsRuntimeScoped: el header del atacante (OtherTenant)
        // habría pasado intacto. Con el fix, el tenant SIEMPRE sale del JWT del caller.
        ctx.Request.Headers["X-Tenant-Id"].ToString().Should().Be(CompanyTenant);
        ctx.Items[TenantEnforcementMiddleware.TenantItemKey].Should().Be(Guid.Parse(CompanyTenant));
        ctx.Items[TenantEnforcementMiddleware.SuperAdminItemKey].Should().Be(false);
    }

    [Fact]
    public async Task BiometricValidationEdit_NoAutenticado_Returns401()
    {
        var ctx = Context("/api/v1/tramites/biometric-validations/22222222-2222-2222-2222-222222222222");
        var next = await InvokeAsync(ctx);

        next.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task BiometricValidationResend_CompanyUser_SobreescribeHeaderConTenantDelToken()
    {
        var ctx = Context(
            "/api/v1/tramites/biometric-validations/22222222-2222-2222-2222-222222222222/resend",
            User("AdminCompany", CompanyTenant), headerTenant: OtherTenant);
        var next = await InvokeAsync(ctx);

        next.Should().BeTrue();
        ctx.Request.Headers["X-Tenant-Id"].ToString().Should().Be(CompanyTenant);
        ctx.Items[TenantEnforcementMiddleware.TenantItemKey].Should().Be(Guid.Parse(CompanyTenant));
        ctx.Items[TenantEnforcementMiddleware.SuperAdminItemKey].Should().Be(false);
    }

    [Fact]
    public async Task BiometricValidationResend_NoAutenticado_Returns401()
    {
        var ctx = Context(
            "/api/v1/tramites/biometric-validations/22222222-2222-2222-2222-222222222222/resend");
        var next = await InvokeAsync(ctx);

        next.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task BiometricValidationEdit_SuperAdmin_ConHeader_AcotaAEsaCompania()
    {
        // El superadmin sigue pudiendo acotar por header (comportamiento existente, sin cambios).
        var ctx = Context("/api/v1/tramites/biometric-validations/22222222-2222-2222-2222-222222222222",
            User("SuperAdmin", tenantId: CompanyTenant), headerTenant: OtherTenant);
        var next = await InvokeAsync(ctx);

        next.Should().BeTrue();
        ctx.Items[TenantEnforcementMiddleware.TenantItemKey].Should().Be(Guid.Parse(OtherTenant));
    }

    // HU #10955 (AC5) — lookup de contacto de actores por documento: sin instancia en la ruta, pero
    // igual de tenant-scoped que el resto del runtime (mismo bug de fondo que b68b71e3 si se quedara
    // fuera de IsRuntimeScoped: el operador podría leer el contacto capturado por OTRA compañía
    // cambiando X-Tenant-Id).
    [Fact]
    public async Task ActorContactLookup_CompanyUser_SobreescribeHeaderConTenantDelToken()
    {
        var ctx = Context("/api/v1/tramites/actors/contact-lookup",
            User("AdminCompany", CompanyTenant), headerTenant: OtherTenant);
        var next = await InvokeAsync(ctx);

        next.Should().BeTrue();
        ctx.Request.Headers["X-Tenant-Id"].ToString().Should().Be(CompanyTenant);
        ctx.Items[TenantEnforcementMiddleware.TenantItemKey].Should().Be(Guid.Parse(CompanyTenant));
        ctx.Items[TenantEnforcementMiddleware.SuperAdminItemKey].Should().Be(false);
    }

    [Fact]
    public async Task ActorContactLookup_NoAutenticado_Returns401()
    {
        var ctx = Context("/api/v1/tramites/actors/contact-lookup");
        var next = await InvokeAsync(ctx);

        next.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(401);
    }
}
