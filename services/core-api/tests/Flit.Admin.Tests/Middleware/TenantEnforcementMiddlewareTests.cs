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
}
