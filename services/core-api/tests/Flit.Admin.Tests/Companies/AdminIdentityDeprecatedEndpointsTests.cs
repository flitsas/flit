using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.Companies;

/// <summary>
/// HU #11758 (ADR-0050) — las diez rutas de disparo/vinculación de identidad administrativa
/// (representante legal send/resend/link; mandatario por OT send/resend/link/mock; mandatario por
/// compañía send/resend/link) responden 410 Gone con <c>code: endpoint_deprecado</c>, NUNCA 404: el
/// módulo Identidad es la única fuente que puede originar una validación. Se verifica también que el
/// CRUD de mandatarios de la compañía (fuera de <c>/identity/*</c>) sigue intacto.
/// </summary>
public sealed class AdminIdentityDeprecatedEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid TenantId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid SomeId = Guid.NewGuid();
    private static readonly Guid TransitOfficeId = Guid.NewGuid();

    private readonly WebApplicationFactory<Program> _factory;

    public AdminIdentityDeprecatedEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    public static IEnumerable<object[]> LegalRepresentativeRoutes()
    {
        yield return new object[] { "send" };
        yield return new object[] { "resend" };
        yield return new object[] { "link" };
    }

    [Theory]
    [MemberData(nameof(LegalRepresentativeRoutes))]
    public async Task RepresentanteLegal_RutasIdentidad_Responden410(string accion)
    {
        var client = SuperAdminClient();
        var response = await client.PostAsync(
            $"/api/v1/admin/companies/{TenantId}/legal-representatives/{SomeId}/identity/{accion}",
            content: null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        await AssertEndpointDeprecadoAsync(response);
    }

    public static IEnumerable<object[]> MandateSignerByOtRoutes()
    {
        yield return new object[] { "send" };
        yield return new object[] { "resend" };
        yield return new object[] { "link" };
        yield return new object[] { "mock" };
    }

    [Theory]
    [MemberData(nameof(MandateSignerByOtRoutes))]
    public async Task MandatarioPorOt_RutasIdentidad_Responden410(string accion)
    {
        var client = OtAdminClient();
        var response = await client.PostAsync(
            $"/api/v1/admin/transit-offices/{TransitOfficeId}/mandate-signers/{SomeId}/identity/{accion}",
            content: null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        await AssertEndpointDeprecadoAsync(response);
    }

    public static IEnumerable<object[]> MandateSignerByCompanyRoutes()
    {
        yield return new object[] { "send" };
        yield return new object[] { "resend" };
        yield return new object[] { "link" };
    }

    [Theory]
    [MemberData(nameof(MandateSignerByCompanyRoutes))]
    public async Task MandatarioPorCompania_RutasIdentidad_Responden410(string accion)
    {
        var client = SuperAdminClient();
        var response = await client.PostAsync(
            $"/api/v1/admin/companies/{TenantId}/mandate-signers/{SomeId}/identity/{accion}",
            content: null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        await AssertEndpointDeprecadoAsync(response);
    }

    /// <summary>
    /// Un AdminCompany de OTRO tenant sigue recibiendo 403 (no 410): el retiro de la ruta no debilita
    /// el aislamiento por tenant de <see cref="Flit.Api.Authorization.CompanyOwnTenantFilter"/>.
    /// </summary>
    [Fact]
    public async Task RepresentanteLegal_RutaIdentidad_AdminCompanyDeOtroTenant_Sigue403()
    {
        var otroTenant = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateAdminCompanyToken(otroTenant));

        var response = await client.PostAsync(
            $"/api/v1/admin/companies/{TenantId}/legal-representatives/{SomeId}/identity/send",
            content: null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>El CRUD de mandatarios de la compañía, fuera de <c>/identity/*</c>, sigue intacto.</summary>
    [Fact]
    public async Task MandatariosDeLaCompania_Crud_SigueDisponible_NoResponde410()
    {
        var client = SuperAdminClient();

        var list = await client.GetAsync(
            $"/api/v1/admin/companies/{TenantId}/mandate-signers",
            TestContext.Current.CancellationToken);
        list.StatusCode.Should().NotBe(HttpStatusCode.Gone);

        var transitOffices = await client.GetAsync(
            $"/api/v1/admin/companies/{TenantId}/mandate-signers/transit-offices",
            TestContext.Current.CancellationToken);
        transitOffices.StatusCode.Should().NotBe(HttpStatusCode.Gone);

        var representedCompanies = await client.GetAsync(
            $"/api/v1/admin/companies/{TenantId}/mandate-signers/represented-companies",
            TestContext.Current.CancellationToken);
        representedCompanies.StatusCode.Should().NotBe(HttpStatusCode.Gone);

        // Inactivate/Reactivate sobre un id inexistente: 404 de negocio, nunca 410 (la ruta sigue viva).
        var inactivate = await client.PostAsync(
            $"/api/v1/admin/companies/{TenantId}/mandate-signers/{SomeId}/inactivate",
            content: null,
            TestContext.Current.CancellationToken);
        inactivate.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var reactivate = await client.PostAsync(
            $"/api/v1/admin/companies/{TenantId}/mandate-signers/{SomeId}/reactivate",
            content: null,
            TestContext.Current.CancellationToken);
        reactivate.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task AssertEndpointDeprecadoAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<DeprecatedErrorEnvelope>(
            cancellationToken: TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        body!.Errors.Should().ContainSingle(e => e.Code == "endpoint_deprecado");
    }

    private HttpClient SuperAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("SuperAdmin"));
        return client;
    }

    private HttpClient OtAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateOtAdminToken(TransitOfficeId));
        return client;
    }

    private sealed record DeprecatedErrorEnvelope(DeprecatedError[] Errors);

    private sealed record DeprecatedError(string? Field, string Code, string Message);
}
