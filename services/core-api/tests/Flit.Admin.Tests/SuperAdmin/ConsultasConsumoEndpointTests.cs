using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Api.Consultas;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.SuperAdmin;

/// <summary>
/// HU #13345 (Epic #13316) — <c>GET /api/v1/superadmin/consultas/consumo</c>: el SuperAdmin ve el consumo de UNA empresa
/// por producto y fuente (AC2); un AdminCompany recibe 403 (AC3). Consultas se reemplaza por un falso.
/// </summary>
public sealed class ConsultasConsumoEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly SymmetricSecurityKey DummyKey = new(Encoding.UTF8.GetBytes("clave-de-prueba-de-al-menos-32-bytes!!"));
    private readonly Guid _empresaA = Guid.NewGuid();
    private readonly Guid _empresaB = Guid.NewGuid();

    [Fact]
    public async Task SuperAdmin_RecibeSoloLosTotalesDeLaEmpresaPedida()
    {
        var client = Client(new ConsumoFalso(_empresaA, _empresaB), "SuperAdmin");

        var response = await client.GetAsync(Url(_empresaA), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var filas = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        filas.GetArrayLength().Should().Be(1);
        filas[0].GetProperty("producto").GetString().Should().Be("tramites");
        filas[0].GetProperty("fuente").GetString().Should().Be("vehiculo");
        filas[0].GetProperty("total").GetInt64().Should().Be(7);
    }

    [Fact]
    public async Task AdminCompany_Recibe403()
    {
        var response = await Client(new ConsumoFalso(_empresaA, _empresaB), "AdminCompany").GetAsync(Url(_empresaA), TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ConConsultasRemotoApagado_503ConSuCodigo()
    {
        var response = await Client(consumo: null, "SuperAdmin").GetAsync(Url(_empresaA), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("CONSULTAS_REMOTO_APAGADO");
    }

    private static string Url(Guid tenant) =>
        $"/api/v1/superadmin/consultas/consumo?tenantId={tenant}&desde=2026-10-01T00:00:00Z&hasta=2026-11-01T00:00:00Z";

    private HttpClient Client(IConsultasConsumo? consumo, string role)
    {
        var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            if (consumo is not null)
                s.AddScoped(_ => consumo);
        }));
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(role));
        return client;
    }

    private static string Token(string role) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = "https://api.flit.co",
        Audience = "flit-api",
        Subject = new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString()), new Claim("role", role), new Claim("tenant_id", Guid.NewGuid().ToString())]),
        Expires = DateTime.UtcNow.AddHours(1),
        SigningCredentials = new SigningCredentials(DummyKey, SecurityAlgorithms.HmacSha256),
    });

    private sealed class ConsumoFalso(Guid a, Guid b) : IConsultasConsumo
    {
        public Task<IReadOnlyList<ConsumoConsultasDto>> ObtenerAsync(Guid tenantId, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ConsumoConsultasDto>>(
                tenantId == a ? [new("tramites", "vehiculo", 7, 2, 0, 120)]
                : tenantId == b ? [new("ict", "conductor", 3, 0, 1, 90)]
                : []);
    }
}
