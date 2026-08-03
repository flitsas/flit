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

namespace Flit.Admin.Tests.DocumentRequirementOverrides;

/// <summary>
/// Seguridad de los endpoints de obligatoriedad por OT (HU #10198; HU #10881): sin token → 401;
/// rol fuera del módulo OT → 403. Desde HU #10881 el grupo admite SuperAdmin u ot_admin, pero un
/// ot_admin queda acotado a SU propia OT: si el <c>transitOfficeId</c> de la petición no
/// coincide con el <c>transit_office_id</c> resuelto desde su claim <c>tenant_id</c>, el guard
/// responde 403 <c>TRANSIT_OFFICE_FORBIDDEN</c> antes de tocar la base de datos (mismo mecanismo
/// que la cola Quipux, HU #10774). El SuperAdmin sigue siendo cross-tenant (cualquier OT).
/// </summary>
public sealed class AdminDocumentRequirementOverridesAuthorizationTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Url = "/api/v1/admin/document-requirement-overrides";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminDocumentRequirementOverridesAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task List_WithoutAuthorizationHeader_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"{Url}?procedureTypeId={Guid.NewGuid()}&transitOfficeId={Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Set_WithoutAuthorizationHeader_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(Url, new { procedureTypeId = Guid.NewGuid(), documentTypeId = Guid.NewGuid(), transitOfficeId = Guid.NewGuid(), estado = "REQUIRED" }, cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_WithRoleOutsideOtModule_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.GetAsync($"{Url}?procedureTypeId={Guid.NewGuid()}&transitOfficeId={Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Set_WithRoleOutsideOtModule_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.PutAsJsonAsync(Url, new { procedureTypeId = Guid.NewGuid(), documentTypeId = Guid.NewGuid(), transitOfficeId = Guid.NewGuid(), estado = "REQUIRED" }, cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_WithSuperAdmin_AnyTransitOffice_PassesScope()
    {
        // SuperAdmin es cross-tenant: no queda cortado por el guard de scope (comportamiento
        // previo a HU #10881 intacto). El 400/404 posterior depende de datos, nunca 401/403.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("SuperAdmin"));

        var response = await client.GetAsync(
            $"{Url}?procedureTypeId={Guid.NewGuid()}&transitOfficeId={Guid.NewGuid()}",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_WithOtAdminForeignTransitOffice_Returns403WithCode()
    {
        // ot_admin cuyo tenant no resuelve a la OT consultada: pasa OtModulePolicy pero el guard
        // de scope lo corta (perfil inexistente o con otro transit_office_id) — caso IDOR.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokenFactory.CreateOtAdminToken(Guid.NewGuid()));

        var response = await client.GetAsync(
            $"{Url}?procedureTypeId={Guid.NewGuid()}&transitOfficeId={Guid.NewGuid()}",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<CodeBody>(TestContext.Current.CancellationToken);
        body!.Code.Should().Be("TRANSIT_OFFICE_FORBIDDEN");
    }

    [Fact]
    public async Task Set_WithOtAdminForeignTransitOffice_Returns403WithCode()
    {
        // Mismo guard aplica al PUT: un ot_admin no configura el override de otra OT — caso IDOR
        // (el más importante de esta HU).
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokenFactory.CreateOtAdminToken(Guid.NewGuid()));

        var response = await client.PutAsJsonAsync(
            Url,
            new
            {
                procedureTypeId = Guid.NewGuid(),
                documentTypeId = Guid.NewGuid(),
                transitOfficeId = Guid.NewGuid(),
                estado = "REQUIRED",
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<CodeBody>(TestContext.Current.CancellationToken);
        body!.Code.Should().Be("TRANSIT_OFFICE_FORBIDDEN");
    }

    [Fact]
    public async Task List_WithOtAdminOwnTransitOffice_PassesScope()
    {
        // ot_admin de SU propia OT: su perfil (por tenant_id) apunta al transit_office_id de la
        // petición, así que el guard lo deja pasar. Se siembra el par tenant→office con Guids
        // frescos para no depender de datos externos. El desenlace exacto depende de la BD
        // (200/400 según exista el trámite), pero NUNCA debe ser 401/403.
        var ownTenantId = Guid.NewGuid();
        var ownOfficeId = Guid.NewGuid();
        await SeedProfileAsync(ownTenantId, ownOfficeId);

        try
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", TestTokenFactory.CreateOtAdminToken(ownTenantId));

            var response = await client.GetAsync(
                $"{Url}?procedureTypeId={Guid.NewGuid()}&transitOfficeId={ownOfficeId}",
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
    public async Task Set_WithOtAdminOwnTransitOffice_PersistsOverrideLikeBefore()
    {
        // ot_admin de SU propia OT configurando el override: el guard lo deja pasar y el
        // comportamiento funcional (upsert/limpieza) sigue igual que antes de HU #10881 —
        // se prueba con datos inexistentes: pasa el guard de scope y llega al handler, que
        // responde 404 (trámite/documento inexistentes) igual que le respondería a un
        // SuperAdmin con la misma petición, NUNCA 401/403.
        var ownTenantId = Guid.NewGuid();
        var ownOfficeId = Guid.NewGuid();
        await SeedProfileAsync(ownTenantId, ownOfficeId);

        try
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", TestTokenFactory.CreateOtAdminToken(ownTenantId));

            var response = await client.PutAsJsonAsync(
                Url,
                new
                {
                    procedureTypeId = Guid.NewGuid(),
                    documentTypeId = Guid.NewGuid(),
                    transitOfficeId = ownOfficeId,
                    estado = "REQUIRED",
                },
                TestContext.Current.CancellationToken);

            response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
            response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            await RemoveProfileAsync(ownTenantId);
        }
    }

    // Siembra un perfil OT (tenant_id → transit_office_id) directo en la BD real que usa la app.
    // El rol de core-api es OWNER de la tabla (sin FORCE RLS), así que el insert directo no
    // requiere fijar app.current_tenant_id. El perfil tiene FK a identity.tenants y a
    // catalogs.transit_offices, así que ambos padres se crean primero.
    private async Task SeedProfileAsync(Guid tenantId, Guid transitOfficeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Code = $"OT-DRO-{Guid.NewGuid():N}"[..20],
            LegalName = "OT DocReqOverrides auth tests",
            TaxId = TestNit.Unique(),
            TenantType = "RENTING",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.TransitOffices.Add(new TransitOffice
        {
            Id = transitOfficeId,
            Code = $"D{Guid.NewGuid():N}"[..10],
            Name = "OT DocReqOverrides auth tests",
            DepartmentCode = "99",
            CityCode = "99998",
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
