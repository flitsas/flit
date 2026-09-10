using System.Security.Claims;
using System.Text;
using Flit.Api.Authorization;
using Flit.Api.Middleware;
using Flit.Queries.Domain.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.Middleware;

/// <summary>
/// HU #12321 (Feature #12254) — canal nuevo <c>tramites.tenantScope</c> del
/// <see cref="TenantEnforcementMiddleware"/>: alcance tipado calculado SOLO desde
/// <see cref="ITenantScopeResolver"/> (BD), cerrado por defecto, sin alterar los Items existentes.
/// Uso de ejemplo:
/// <code>
/// var scope = RequestTenantResolver.ScopeFromItems(http); // null = ruta no scopeada
/// if (scope is { IsGroup: true }) query = query.WhereTenantInScope(scope, x => x.TenantId);
/// </code>
/// </summary>
public sealed class TenantEnforcementMiddlewareScopeTests
{
    private static readonly Guid CompanyTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Child1 = Guid.Parse("22222222-2222-2222-2222-222222222221");
    private static readonly Guid Child2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherTenant = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private sealed class FakeResolver(Func<Guid, TenantScope> resolve) : ITenantScopeResolver
    {
        public List<Guid> Calls { get; } = [];

        public Task<TenantScope> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            Calls.Add(tenantId);
            return Task.FromResult(resolve(tenantId));
        }
    }

    private static DefaultHttpContext Context(
        string path,
        ClaimsPrincipal? user,
        ITenantScopeResolver? resolver,
        Action<DefaultHttpContext>? configure = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Response.Body = new MemoryStream();
        if (user is not null)
            ctx.User = user;

        var services = new ServiceCollection();
        if (resolver is not null)
            services.AddScoped<ITenantScopeResolver>(_ => resolver);
        ctx.RequestServices = services.BuildServiceProvider();

        configure?.Invoke(ctx);
        return ctx;
    }

    private static ClaimsPrincipal User(string role, Guid? tenantId)
    {
        var claims = new List<Claim> { new("role", role) };
        if (tenantId is not null)
            claims.Add(new Claim("tenant_id", tenantId.Value.ToString()));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
    }

    private static async Task<bool> InvokeAsync(DefaultHttpContext ctx)
    {
        var nextCalled = false;
        var mw = new TenantEnforcementMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        await mw.InvokeAsync(ctx);
        return nextCalled;
    }

    // ── AC1 — cliente sin jerarquía ⇒ Single(propio); Items previos intactos ───

    [Fact]
    public async Task CompanyUser_SinJerarquia_DejaSingleDelPropio_YConservaItemsExistentes()
    {
        var resolver = new FakeResolver(id => TenantScope.Single(id));
        var ctx = Context("/api/v1/tramites/instances", User("AdminCompany", CompanyTenant), resolver);

        var next = await InvokeAsync(ctx);

        next.Should().BeTrue();
        var scope = RequestTenantResolver.ScopeFromItems(ctx);
        scope.Should().NotBeNull();
        scope!.IsAll.Should().BeFalse();
        scope.IsGroup.Should().BeFalse();
        scope.WriteTenantId.Should().Be(CompanyTenant);
        scope.ReadTenantIds.Should().BeEquivalentTo([CompanyTenant]);
        // Canal existente: exactamente igual que antes de la HU.
        ctx.Items[TenantEnforcementMiddleware.TenantItemKey].Should().Be(CompanyTenant);
        ctx.Items[TenantEnforcementMiddleware.SuperAdminItemKey].Should().Be(false);
        RequestTenantResolver.FromItems(ctx).Should().Be((CompanyTenant, false));
        resolver.Calls.Should().Equal(CompanyTenant);
    }

    // ── AC2 — cabeza de grupo con dos hijos ⇒ Group; tramites.tenantId sigue siendo el padre ──

    [Fact]
    public async Task CompanyUser_CabezaDeGrupo_DejaGroup_YTenantIdSigueSiendoElPadre()
    {
        var resolver = new FakeResolver(id => TenantScope.Group(id, [Child1, Child2]));
        var ctx = Context("/api/v1/tramites/instances", User("AdminCompany", CompanyTenant), resolver);

        var next = await InvokeAsync(ctx);

        next.Should().BeTrue();
        var scope = RequestTenantResolver.ScopeFromItems(ctx)!;
        scope.IsGroup.Should().BeTrue();
        scope.ReadTenantIds.Should().BeEquivalentTo([CompanyTenant, Child1, Child2]);
        scope.WriteTenantId.Should().Be(CompanyTenant);
        // AC2: nunca null, nunca un hijo.
        ctx.Items[TenantEnforcementMiddleware.TenantItemKey].Should().Be(CompanyTenant);
        ctx.Items[TenantEnforcementMiddleware.SuperAdminItemKey].Should().Be(false);
        ctx.Request.Headers["X-Tenant-Id"].ToString().Should().Be(CompanyTenant.ToString());
    }

    // ── AC3 — fallo del resolver ⇒ Single(propio); jamás All ───────────────────

    [Fact]
    public async Task CompanyUser_ResolverLanza_DejaSingleDelPropio_NuncaAll()
    {
        var resolver = new FakeResolver(_ => throw new InvalidOperationException("BD caída"));
        var ctx = Context("/api/v1/tramites/instances", User("AdminCompany", CompanyTenant), resolver);

        var next = await InvokeAsync(ctx);

        next.Should().BeTrue("el fallo del resolver no rompe la petición: degrada a alcance propio");
        var scope = RequestTenantResolver.ScopeFromItems(ctx)!;
        scope.IsAll.Should().BeFalse();
        scope.IsGroup.Should().BeFalse();
        scope.ReadTenantIds.Should().BeEquivalentTo([CompanyTenant]);
        ctx.Items[TenantEnforcementMiddleware.TenantItemKey].Should().Be(CompanyTenant);
    }

    [Fact]
    public async Task CompanyUser_SinResolverRegistrado_DejaSingleDelPropio()
    {
        var ctx = Context("/api/v1/tramites/instances", User("AdminCompany", CompanyTenant), resolver: null);

        var next = await InvokeAsync(ctx);

        next.Should().BeTrue();
        var scope = RequestTenantResolver.ScopeFromItems(ctx)!;
        scope.IsAll.Should().BeFalse();
        scope.ReadTenantIds.Should().BeEquivalentTo([CompanyTenant]);
    }

    [Fact]
    public async Task CompanyUser_SinRequestServices_DejaSingleDelPropio()
    {
        // Los tests históricos del middleware no configuran RequestServices: no deben romperse.
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/v1/tramites/instances";
        ctx.Response.Body = new MemoryStream();
        ctx.User = User("AdminCompany", CompanyTenant);

        var next = await InvokeAsync(ctx);

        next.Should().BeTrue();
        RequestTenantResolver.ScopeFromItems(ctx)!.ReadTenantIds.Should().BeEquivalentTo([CompanyTenant]);
    }

    [Fact]
    public async Task CompanyUser_ResolverDevuelveAll_ElMiddlewareLoDegradaASingle()
    {
        // Defensa en profundidad: aunque un resolver malicioso/defectuoso fabricara All (solo posible
        // con InternalsVisibleTo), el middleware NUNCA lo entrega a un company-user.
        var resolver = new FakeResolver(_ => TenantScope.All());
        var ctx = Context("/api/v1/tramites/instances", User("AdminCompany", CompanyTenant), resolver);

        await InvokeAsync(ctx);

        var scope = RequestTenantResolver.ScopeFromItems(ctx)!;
        scope.IsAll.Should().BeFalse();
        scope.ReadTenantIds.Should().BeEquivalentTo([CompanyTenant]);
    }

    // ── AC4 — el alcance no viaja en la petición ────────────────────────────────

    [Fact]
    public async Task CompanyUser_HeaderXTenantIdsYBodyConLista_SeIgnoran_SoloCuentaLaBD()
    {
        var resolver = new FakeResolver(id => TenantScope.Single(id));
        var ctx = Context("/api/v1/tramites/instances", User("AdminCompany", CompanyTenant), resolver, http =>
        {
            http.Request.Headers["X-Tenant-Ids"] = $"{CompanyTenant},{OtherTenant},{Child1}";
            http.Request.Headers["X-Tenant-Id"] = OtherTenant.ToString();
            http.Request.Headers["X-Tenant-Scope"] = "all";
            http.Request.ContentType = "application/json";
            http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
                $$"""{"tenantIds":["{{OtherTenant}}","{{Child1}}"],"scope":"all"}"""));
        });

        var next = await InvokeAsync(ctx);

        next.Should().BeTrue();
        var scope = RequestTenantResolver.ScopeFromItems(ctx)!;
        scope.IsAll.Should().BeFalse();
        scope.ReadTenantIds.Should().BeEquivalentTo([CompanyTenant], "solo la BD decide el alcance");
        scope.CanRead(OtherTenant).Should().BeFalse();
        scope.CanRead(Child1).Should().BeFalse();
        // El resolver recibió únicamente el tenant del JWT, nunca lo que mandó el cliente.
        resolver.Calls.Should().Equal(CompanyTenant);
        ctx.Request.Headers["X-Tenant-Id"].ToString().Should().Be(CompanyTenant.ToString());
        ctx.Request.Body.Position.Should().Be(0, "el middleware no lee el body");
    }

    [Fact]
    public async Task CompanyUser_ClaimConListaDeTenants_SeIgnora()
    {
        // Un token con claims extra ("tenant_ids"/"tenant_scope") no amplía el alcance: no existe tal claim
        // en el emisor y el middleware solo pasa tenant_id al resolver.
        var resolver = new FakeResolver(id => TenantScope.Single(id));
        var claims = new List<Claim>
        {
            new("role", "AdminCompany"),
            new("tenant_id", CompanyTenant.ToString()),
            new("tenant_ids", $"{CompanyTenant},{OtherTenant}"),
            new("tenant_scope", "all"),
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
        var ctx = Context("/api/v1/tramites/instances", user, resolver);

        await InvokeAsync(ctx);

        var scope = RequestTenantResolver.ScopeFromItems(ctx)!;
        scope.IsAll.Should().BeFalse();
        scope.ReadTenantIds.Should().BeEquivalentTo([CompanyTenant]);
        resolver.Calls.Should().Equal(CompanyTenant);
    }

    // ── AC7 — SuperAdmin ⇒ All(); capacidades intactas ─────────────────────────

    [Fact]
    public async Task SuperAdmin_SinHeader_DejaAll_YNoConsultaElResolver()
    {
        var resolver = new FakeResolver(id => TenantScope.Single(id));
        var ctx = Context("/api/v1/tramites/instances", User("SuperAdmin", tenantId: null), resolver);

        var next = await InvokeAsync(ctx);

        next.Should().BeTrue();
        var scope = RequestTenantResolver.ScopeFromItems(ctx)!;
        scope.IsAll.Should().BeTrue();
        scope.CanRead(OtherTenant).Should().BeTrue();
        scope.CanWrite(OtherTenant).Should().BeTrue();
        resolver.Calls.Should().BeEmpty("SuperAdmin no depende de la jerarquía");
        // Canal existente intacto.
        ctx.Items[TenantEnforcementMiddleware.SuperAdminItemKey].Should().Be(true);
        ctx.Items[TenantEnforcementMiddleware.TenantItemKey].Should().BeNull();
    }

    [Fact]
    public async Task SuperAdmin_ConHeader_SigueAcotandoTenantId_YScopeEsAll()
    {
        var resolver = new FakeResolver(id => TenantScope.Single(id));
        var ctx = Context("/api/v1/tramites/instances", User("SuperAdmin", CompanyTenant), resolver,
            http => http.Request.Headers["X-Tenant-Id"] = OtherTenant.ToString());

        await InvokeAsync(ctx);

        RequestTenantResolver.ScopeFromItems(ctx)!.IsAll.Should().BeTrue();
        ctx.Items[TenantEnforcementMiddleware.TenantItemKey].Should().Be(OtherTenant,
            "el SuperAdmin conserva su capacidad de acotar por header (sin cambios)");
    }

    // ── AC8 / ruta fuera de la lista ⇒ sin scope ───────────────────────────────

    [Fact]
    public async Task RutaNoRuntimeScoped_NoDejaScope_NiLlamaAlResolver()
    {
        var resolver = new FakeResolver(id => TenantScope.Single(id));
        var ctx = Context("/api/v1/tramites/procedure-types", User("AdminCompany", CompanyTenant), resolver);

        var next = await InvokeAsync(ctx);

        next.Should().BeTrue();
        RequestTenantResolver.ScopeFromItems(ctx).Should().BeNull();
        ctx.Items.Should().NotContainKey(TenantEnforcementMiddleware.TenantScopeItemKey);
        ctx.Items.Should().NotContainKey(TenantEnforcementMiddleware.TenantItemKey);
        resolver.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task NoAutenticado_401_NoDejaScope()
    {
        var resolver = new FakeResolver(id => TenantScope.Single(id));
        var ctx = Context("/api/v1/tramites/instances", user: null, resolver);

        var next = await InvokeAsync(ctx);

        next.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(401);
        RequestTenantResolver.ScopeFromItems(ctx).Should().BeNull();
        resolver.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task CompanyUser_SinTenantEnToken_403_NoDejaScope()
    {
        var resolver = new FakeResolver(id => TenantScope.Single(id));
        var ctx = Context("/api/v1/tramites/instances", User("AdminCompany", tenantId: null), resolver);

        var next = await InvokeAsync(ctx);

        next.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(403);
        RequestTenantResolver.ScopeFromItems(ctx).Should().BeNull();
        resolver.Calls.Should().BeEmpty();
    }

    // ── Contrato del accessor ──────────────────────────────────────────────────

    [Fact]
    public void ScopeFromItems_ItemConTipoAjeno_DevuelveNull()
    {
        var ctx = new DefaultHttpContext();
        ctx.Items[TenantEnforcementMiddleware.TenantScopeItemKey] = "no-es-un-scope";

        RequestTenantResolver.ScopeFromItems(ctx).Should().BeNull();
    }

    [Fact]
    public void ScopeFromItems_HttpNull_Lanza()
    {
        var act = () => RequestTenantResolver.ScopeFromItems(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
