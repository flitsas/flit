using System.Net;
using System.Text.Json;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Tramites;

/// <summary>
/// HU #13303 (Épica #13202) — <c>GET /api/v1/tramites/biometric-validations/{id}</c> (el que consume el detalle de Identidad)
/// devuelve <c>approvalOrigin</c> con el pipeline HTTP real y PostgreSQL real: <c>automatica</c>, <c>manual</c> o null.
/// </summary>
public sealed class ApprovalOriginEndpointTests : IClassFixture<ManualReviewFactory>, IDisposable
{
    private readonly ManualReviewHost _host;

    public ApprovalOriginEndpointTests(ManualReviewFactory factory) => _host = new ManualReviewHost(factory);

    public void Dispose() => _host.Dispose();

    private static CancellationToken Ct => ManualReviewHost.Ct;

    private async Task<JsonElement?> DetalleAsync(Guid id)
    {
        _host.AuthenticateSuperAdmin();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/tramites/biometric-validations/{id}");
        request.Headers.Add("X-Tenant-Id", _host.Dueno.ToString());
        var response = await _host.Client.SendAsync(request, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    [Theory]
    [InlineData(BiometricApprovalOrigins.Automatica)]
    [InlineData(BiometricApprovalOrigins.Manual)]
    public async Task El_detalle_de_una_aprobada_trae_su_origen(string origen)
    {
        var seeded = await _host.SeedAsync(
            BiometricEstados.Aprobado, BiometricProviders.Mock, conImagenes: false, approvalOrigin: origen);

        var json = await DetalleAsync(seeded.Id);

        json!.Value.GetProperty("approvalOrigin").GetString().Should().Be(origen);
        json.Value.GetRawText().Should().NotContain(_host.SuperAdmin.ToString(), "nunca trae quién revisó");
    }

    [Fact]
    public async Task El_detalle_de_una_no_aprobada_trae_origen_null()
    {
        var seeded = await _host.SeedAsync(BiometricEstados.Rechazado, BiometricProviders.Mock, conImagenes: false);

        var json = await DetalleAsync(seeded.Id);

        json!.Value.GetProperty("approvalOrigin").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task El_detalle_de_una_rechazada_manual_trae_el_codigo_del_motivo()
    {
        var seeded = await _host.SeedAsync(
            BiometricEstados.Rechazado, BiometricProviders.Manual, conImagenes: false, rejectionReasonCode: "imagen_borrosa");

        var json = await DetalleAsync(seeded.Id);

        json!.Value.GetProperty("rejectionReasonCode").GetString().Should().Be("imagen_borrosa");
    }

    [Theory]
    [InlineData(BiometricProviders.Mock)]
    [InlineData(BiometricProviders.Kyverum)]
    public async Task El_detalle_de_una_rechazada_no_manual_trae_codigo_null(string provider)
    {
        var seeded = await _host.SeedAsync(BiometricEstados.Rechazado, provider, conImagenes: false);

        var json = await DetalleAsync(seeded.Id);

        json!.Value.GetProperty("rejectionReasonCode").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
