using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Admin.Tests.Companies;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.Companies.TransitOffices;

/// <summary>
/// Seguridad de la cola Quipux de una secretaría (HU #10774): GET/retry/cancel de
/// <c>/api/v1/admin/transit-offices/{id}/quipux-submissions</c>. A diferencia de la
/// parametrización del catálogo (SuperAdmin-only), la cola es operable por ot_admin, pero
/// acotada a SU propia secretaría: el <c>{id}</c> de la ruta debe coincidir con el claim
/// <c>tenant_id</c> del token, o el guard responde 403 <c>TRANSIT_OFFICE_FORBIDDEN</c> antes
/// de tocar la base de datos. El SuperAdmin es cross-tenant (cualquier secretaría).
/// </summary>
public sealed class AdminTransitOfficesQuipuxColaAuthorizationTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid OtId = Guid.NewGuid();
    private static readonly string ListUrl =
        $"/api/v1/admin/transit-offices/{OtId}/quipux-submissions";
    private static readonly string RetryUrl =
        $"/api/v1/admin/transit-offices/{OtId}/quipux-submissions/{Guid.NewGuid()}/retry";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminTransitOfficesQuipuxColaAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task List_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(ListUrl, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_WithRoleOutsideOtModule_Returns403()
    {
        // Un rol que no es SuperAdmin ni ot_admin queda fuera del grupo (OtModulePolicy) antes
        // de llegar al guard de scope.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.GetAsync(ListUrl, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_WithOtAdminForeignTenant_Returns403WithCode()
    {
        // ot_admin cuyo tenant no resuelve a ESTA secretaría: pasa OtModulePolicy pero el guard
        // de scope lo corta (perfil inexistente o con otro transit_office_id).
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokenFactory.CreateOtAdminToken(Guid.NewGuid()));

        var response = await client.GetAsync(ListUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<CodeBody>(TestContext.Current.CancellationToken);
        body!.Code.Should().Be("TRANSIT_OFFICE_FORBIDDEN");
    }

    [Fact]
    public async Task List_WithOtAdminOwnOffice_PassesScope()
    {
        // ot_admin de SU secretaría: su perfil (por tenant_id) apunta al transit_office_id de la
        // ruta, así que el guard lo deja pasar. Se siembra el par tenant→office con Guids frescos
        // para NO depender de datos externos (el seed de dev no existe en la BD limpia del CI;
        // tenant_id ≠ transit_office_id). El desenlace exacto depende de la BD, pero NUNCA debe
        // ser 401/403.
        var ownTenantId = Guid.NewGuid();
        var ownOfficeId = Guid.NewGuid();
        await SeedProfileAsync(ownTenantId, ownOfficeId);

        try
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", TestTokenFactory.CreateOtAdminToken(ownTenantId));

            var response = await client.GetAsync(
                $"/api/v1/admin/transit-offices/{ownOfficeId}/quipux-submissions",
                TestContext.Current.CancellationToken);

            response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
            response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        }
        finally
        {
            await RemoveProfileAsync(ownTenantId);
        }
    }

    [Fact]
    public async Task List_WithSuperAdmin_PassesScope()
    {
        // SuperAdmin es cross-tenant: opera la cola de cualquier secretaría.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("SuperAdmin"));

        var response = await client.GetAsync(ListUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Retry_WithOtAdminForeignTenant_Returns403()
    {
        // El mismo guard aplica a las operaciones: un ot_admin no re-encola la cola de otra OT.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokenFactory.CreateOtAdminToken(Guid.NewGuid()));

        var response = await client.PostAsync(RetryUrl, content: null, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // Siembra un perfil OT (tenant_id → transit_office_id) directo en la BD real que usa la app.
    // El rol de core-api es OWNER de la tabla (sin FORCE RLS), así que el insert directo no requiere
    // fijar app.current_tenant_id. El perfil tiene FK a identity.tenants y a catalogs.transit_offices,
    // así que ambos padres se crean primero.
    private async Task SeedProfileAsync(Guid tenantId, Guid transitOfficeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Code = $"OT-QX-{Guid.NewGuid():N}"[..20],
            LegalName = "OT Cola QX auth tests",
            TaxId = "900999999-9",
            TenantType = "RENTING",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.TransitOffices.Add(new TransitOffice
        {
            Id = transitOfficeId,
            Code = $"Q{Guid.NewGuid():N}"[..10],
            Name = "OT Cola QX auth tests",
            DepartmentCode = "99",
            CityCode = "99999",
            IsActive = true,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TransitOfficeId = transitOfficeId,
            OperationMode = "dashboard",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task RemoveProfileAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();

        // Orden inverso a las FK: primero el perfil (hijo), luego tenant y oficina (padres).
        var officeIds = db.TransitOfficeProfiles
            .Where(p => p.TenantId == tenantId)
            .Select(p => p.TransitOfficeId)
            .ToList();

        db.TransitOfficeProfiles.RemoveRange(
            db.TransitOfficeProfiles.Where(p => p.TenantId == tenantId));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Tenants.RemoveRange(db.Tenants.Where(t => t.Id == tenantId));
        db.TransitOffices.RemoveRange(db.TransitOffices.Where(o => officeIds.Contains(o.Id)));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private sealed record CodeBody(string Code);
}
