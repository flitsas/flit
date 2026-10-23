using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Admin.Tests.Companies;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.PlatePreassign;

/// <summary>
/// HU #12853 (Feature #12846 "Eliminar módulo de Preasignación de rango", Épica #12751) — la ruta de
/// placa preasignada de la compañía (<c>/api/v1/tramites/plate-preassign/available</c> y
/// <c>/status</c>) se apaga: responde 410 Gone para cualquier usuario de compañía autenticado (AC1).
/// El middleware de tenant runtime (<see cref="Flit.Api.Middleware.TenantEnforcementMiddleware"/>)
/// sigue exigiendo autenticación ANTES de llegar al endpoint (401 sin token).
/// </summary>
public sealed class PlatePreassignRouteGoneEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid TransitOfficeId = Guid.NewGuid();
    private static readonly Guid CompanyTenantId = Guid.NewGuid();

    private readonly WebApplicationFactory<Program> _factory;

    public PlatePreassignRouteGoneEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    public static IEnumerable<object[]> Routes()
    {
        yield return new object[] { $"/api/v1/tramites/plate-preassign/available?transitOfficeId={TransitOfficeId}" };
        yield return new object[] { $"/api/v1/tramites/plate-preassign/status?transitOfficeId={TransitOfficeId}" };
    }

    // HU12853_AC1 — usuario de compañía autenticado: 410 Gone con el sobre estándar de errores.
    [Theory]
    [MemberData(nameof(Routes))]
    public async Task HU12853_AC1_UsuarioDeCompania_Responde410(string ruta)
    {
        var client = CompanyClient();
        var response = await client.GetAsync(ruta, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        var body = await response.Content.ReadFromJsonAsync<DeprecatedErrorEnvelope>(
            cancellationToken: TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        body!.Errors.Should().ContainSingle(e => e.Code == "endpoint_deprecado");
    }

    // El middleware de tenant runtime sigue exigiendo autenticación para este prefijo (ver nota en
    // TenantEnforcementMiddleware.RuntimeScopedRoutes): sin token, 401 ANTES de llegar al 410.
    [Theory]
    [MemberData(nameof(Routes))]
    public async Task HU12853_AC1_SinAutenticar_Responde401NoGone(string ruta)
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(ruta, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private HttpClient CompanyClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateAdminCompanyToken(CompanyTenantId));
        return client;
    }

    private sealed record DeprecatedErrorEnvelope(DeprecatedError[] Errors);

    private sealed record DeprecatedError(string? Field, string Code, string Message);
}
