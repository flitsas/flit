using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Admin.Tests.Companies;
using Flit.Api.Authorization;
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
/// Seguridad de los endpoints de obligatoriedad por OT (HU #10198; HU #10881; HU #12859): sin
/// token → 401; rol fuera del módulo OT → 403. Entre HU #10881 y HU #12859 el grupo admitía
/// SuperAdmin u ot_admin (acotado a SU propia OT vía <see cref="EnforceTransitOfficeScopeAsync"/>,
/// mismo mecanismo que la cola Quipux, HU #10774). HU #12859 (Feature #12848, Épica #12751) cerró
/// el acceso a SuperAdmin EXCLUSIVO: un ot_admin ahora recibe 403 por autorización, incluso en SU
/// propia OT — el guard de scope queda sin alcanzar en runtime para ese rol, pero se conserva como
/// defensa en profundidad. El SuperAdmin sigue siendo cross-tenant (cualquier OT).
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
    public async Task HU12859_AC1_List_AsOtAdmin_ForeignTransitOffice_Returns403()
    {
        // HU #12859: SuperAdminPolicy corta ANTES del guard de scope — ot_admin recibe 403 por
        // autorización, ya no 403 TRANSIT_OFFICE_FORBIDDEN del guard (que queda sin alcanzar).
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokenFactory.CreateOtAdminToken(Guid.NewGuid()));

        var response = await client.GetAsync(
            $"{Url}?procedureTypeId={Guid.NewGuid()}&transitOfficeId={Guid.NewGuid()}",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ForbiddenBody>(TestContext.Current.CancellationToken);
        body!.Error.Should().Be(AdminAuthorization.OtModuleForbiddenMessage);
    }

    [Fact]
    public async Task HU12859_AC1_Set_AsOtAdmin_ForeignTransitOffice_Returns403()
    {
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
    }

    [Fact]
    public async Task HU12859_AC1_List_AsOtAdmin_OwnTransitOffice_Returns403()
    {
        // HU #12859 invierte esta aserción: ANTES de esta HU, un ot_admin de SU PROPIA OT pasaba
        // el guard de scope (ver historial de este archivo). Ahora SuperAdminPolicy lo bloquea
        // igual, sea su propia OT o una ajena — "overrides de exigencias documentales" es de las
        // 3 superficies que HU #12859 deja exclusivas de SuperAdmin (junto con Etiquetas y prenda).
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

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            await RemoveProfileAsync(ownTenantId);
        }
    }

    [Fact]
    public async Task HU12859_AC1_Set_AsOtAdmin_OwnTransitOffice_Returns403()
    {
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

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            await RemoveProfileAsync(ownTenantId);
        }
    }

    [Fact]
    public async Task HU12859_AC3_Set_AsSuperAdmin_PassesScope()
    {
        // SuperAdmin sigue operando esta ruta (AC3): el desenlace exacto depende de datos
        // (404 si trámite/documento no existen), pero NUNCA debe ser 401/403.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("SuperAdmin"));

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

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
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

    private sealed record ForbiddenBody(string Error);
}
