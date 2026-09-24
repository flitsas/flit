using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Admin.Tests.Companies;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.PlatePreassign;

/// <summary>
/// HU #12849 (Feature #12846 "Eliminar módulo de Preasignación de rango", Épica #12751) — la consola
/// de rangos deja de operar en la API (AC2): listar/crear/editar rangos, listar placas, compañías
/// elegibles y bloquear/desbloquear/revocar placa de rango responden 410 Gone para cualquier rol
/// autenticado, nunca 200 ni 404. Las rutas del ciclo de vida del trámite (assign-plate, release-plate,
/// revoke alias, update-plate) se conservan sin cambio de contrato (AC1 y AC3).
/// </summary>
public sealed class AdminPlateRangesConsoleGoneEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid TransitOfficeId = Guid.NewGuid();
    private static readonly Guid CompanyTenantId = Guid.NewGuid();
    private static readonly Guid RangeId = Guid.NewGuid();
    private static readonly Guid PlateId = Guid.NewGuid();
    private static readonly Guid InstanceId = Guid.NewGuid();

    private readonly WebApplicationFactory<Program> _factory;

    public AdminPlateRangesConsoleGoneEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    public static IEnumerable<object[]> ConsoleGetRoutes()
    {
        yield return new object[] { $"/api/v1/admin/plate-ranges/?companyTenantId={CompanyTenantId}" };
        yield return new object[] { $"/api/v1/admin/plate-ranges/plates?companyTenantId={CompanyTenantId}" };
        yield return new object[] { "/api/v1/admin/plate-ranges/eligible-companies" };
    }

    // HU12849_AC2 — GET de la consola responde 410 para ot_admin.
    [Theory]
    [MemberData(nameof(ConsoleGetRoutes))]
    public async Task HU12849_AC2_ConsoleGet_OtAdmin_Responde410(string ruta)
    {
        var client = OtAdminClient();
        var response = await client.GetAsync(ruta, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        await AssertEndpointDeprecadoAsync(response);
    }

    // HU12849_AC2 — GET de la consola responde 410 también para Super Admin: NINGÚN rol la ve viva.
    [Theory]
    [MemberData(nameof(ConsoleGetRoutes))]
    public async Task HU12849_AC2_ConsoleGet_SuperAdmin_Responde410(string ruta)
    {
        var client = SuperAdminClient();
        var response = await client.GetAsync(ruta, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        await AssertEndpointDeprecadoAsync(response);
    }

    // HU12849_AC2 — POST/PUT de creación y edición de rango responden 410.
    [Fact]
    public async Task HU12849_AC2_AssignRange_Responde410()
    {
        var client = OtAdminClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/plate-ranges/",
            new { CompanyTenantId, Prefix = "ABC", RangeFrom = 100, RangeTo = 105 },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        await AssertEndpointDeprecadoAsync(response);
    }

    [Fact]
    public async Task HU12849_AC2_EditRange_Responde410()
    {
        var client = OtAdminClient();
        var response = await client.PutAsJsonAsync(
            $"/api/v1/admin/plate-ranges/{RangeId}",
            new { Prefix = "ABC", RangeFrom = 200, RangeTo = 205 },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        await AssertEndpointDeprecadoAsync(response);
    }

    public static IEnumerable<object[]> PlateStateRoutes()
    {
        yield return new object[] { "block" };
        yield return new object[] { "unblock" };
        yield return new object[] { "revoke" };
    }

    // HU12849_AC2 — bloquear/desbloquear/revocar placa de RANGO (consola) responde 410. No confundir
    // con procedures/{id}/revoke (alias de release-plate del TRÁMITE, AC1/AC3, que sigue vivo).
    [Theory]
    [MemberData(nameof(PlateStateRoutes))]
    public async Task HU12849_AC2_PlateState_Responde410(string accion)
    {
        var client = OtAdminClient();
        var response = await client.PostAsync(
            $"/api/v1/admin/plate-ranges/plates/{PlateId}/{accion}",
            content: null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        await AssertEndpointDeprecadoAsync(response);
    }

    // HU12849_AC1/AC3 — las rutas del ciclo de vida del trámite en preasignacion NO se retiran: pueden
    // fallar por datos inexistentes (401/404/422), pero nunca con 410.
    public static IEnumerable<object[]> ProcedureLifecycleRoutes()
    {
        yield return new object[] { $"/api/v1/admin/plate-ranges/procedures/{InstanceId}/assign-plate", new { Plate = "ABC123" } };
        yield return new object[] { $"/api/v1/admin/plate-ranges/procedures/{InstanceId}/release-plate", new { Reason = "motivo" } };
        yield return new object[] { $"/api/v1/admin/plate-ranges/procedures/{InstanceId}/revoke", new { Reason = "motivo" } };
        yield return new object[] { $"/api/v1/admin/plate-ranges/procedures/{InstanceId}/update-plate", new { Plate = "ABC123" } };
    }

    [Theory]
    [MemberData(nameof(ProcedureLifecycleRoutes))]
    public async Task HU12849_AC3_CicloDeVidaDelTramite_NuncaResponde410(string ruta, object payload)
    {
        var client = OtAdminClient();
        var response = await client.PostAsJsonAsync(ruta, payload, TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Gone);
    }

    private static async Task AssertEndpointDeprecadoAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<DeprecatedErrorEnvelope>(
            cancellationToken: TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        body!.Errors.Should().ContainSingle(e => e.Code == "endpoint_deprecado");
    }

    private HttpClient OtAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateOtAdminToken(TransitOfficeId));
        return client;
    }

    private HttpClient SuperAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("SuperAdmin"));
        return client;
    }

    private sealed record DeprecatedErrorEnvelope(DeprecatedError[] Errors);

    private sealed record DeprecatedError(string? Field, string Code, string Message);
}
