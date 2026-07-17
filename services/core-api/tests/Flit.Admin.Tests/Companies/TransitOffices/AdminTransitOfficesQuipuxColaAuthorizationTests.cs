using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Admin.Tests.Companies;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
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

    // Par sembrado por la migración HU10133_OtAdminDevSeed: el ot_admin cuyo tenant es
    // SeededTenantId tiene su perfil apuntando a SeededOfficeId (tenant_id ≠ transit_office_id).
    private static readonly Guid SeededTenantId = Guid.Parse("bbbbbbbb-0001-4000-8000-000000000001");
    private static readonly Guid SeededOfficeId = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001");

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
        // ruta, así que el guard lo deja pasar. El desenlace exacto depende de la BD, pero NUNCA
        // debe ser 401/403.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokenFactory.CreateOtAdminToken(SeededTenantId));

        var response = await client.GetAsync(
            $"/api/v1/admin/transit-offices/{SeededOfficeId}/quipux-submissions",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
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

    private sealed record CodeBody(string Code);
}
