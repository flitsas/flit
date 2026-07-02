using System.Net;
using System.Net.Http.Headers;
using Flit.Admin.Tests.Companies;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.Improntas;

/// <summary>
/// Tests de seguridad de <c>GET /api/v1/admin/improntas</c> (HU #10468 / AC2 — solo
/// SuperAdmin puede consultar el historial global de improntas). La autorización corta
/// la petición antes de tocar la base de datos: sin token → 401, con token pero sin rol
/// SuperAdmin → 403, sin exponer ningún registro de historial de ningún tenant.
/// </summary>
public sealed class AdminImprontasAuthorizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string IndexUrl = "/api/v1/admin/improntas";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminImprontasAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AC2_Index_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(IndexUrl, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC2_Index_WithNonSuperAdminRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));

        var response = await client.GetAsync(IndexUrl, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AC2_Index_WithOtAdminRole_Returns403()
    {
        // Historial global de improntas: exclusivo de SuperAdmin (sin RLS, ADR-0022) — a
        // diferencia de otros módulos OT, ot_admin NO tiene acceso a este endpoint.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("ot_admin"));

        var response = await client.GetAsync(IndexUrl, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
