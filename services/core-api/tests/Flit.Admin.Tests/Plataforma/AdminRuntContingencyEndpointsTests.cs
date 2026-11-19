using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Plataforma.RuntContingency;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Plataforma;

/// <summary>
/// Épica #14644, HU #14648 — <c>/api/v1/admin/platform/runt-contingency</c>: solo con el permiso <c>runt_contingency.manage</c>
/// (sembrado únicamente para el Super Admin FLIT). El puerto hacia core-consultas y la auditoría se sustituyen por dobles.
/// </summary>
public sealed class AdminRuntContingencyEndpointsTests : IClassFixture<AdminRuntContingencyEndpointsTests.ContingencyFactory>
{
    private const string BaseUrl = "/api/v1/admin/platform/runt-contingency";
    private static readonly Guid SuperAdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly ContingencyFactory _factory;

    public AdminRuntContingencyEndpointsTests(ContingencyFactory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    [Fact]
    public async Task AC1_Get_DevuelveLosTresTiposEnOrden_ConProveedorMotivoUsuarioYFecha()
    {
        _factory.Gateway.Items["vehicle_plate"] = new RuntContingencyItem("vehicle_plate", "verifik", "Kyverum caído", "Ana Pérez", DateTimeOffset.Parse("2026-11-19T14:10:00Z"));
        using var client = ClientWith(SuperAdminToken());

        var response = await client.GetAsync(BaseUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("kind").GetString()).Should().Equal("vehicle_vin", "vehicle_plate", "conductor");
        items[0].GetProperty("provider").ValueKind.Should().Be(JsonValueKind.Null);
        items[1].GetProperty("provider").GetString().Should().Be("verifik");
        items[1].GetProperty("reason").GetString().Should().Be("Kyverum caído");
        items[1].GetProperty("updatedByName").GetString().Should().Be("Ana Pérez");
        items[1].GetProperty("updatedAt").GetDateTimeOffset().Should().Be(DateTimeOffset.Parse("2026-11-19T14:10:00Z"));
    }

    [Fact]
    public async Task AC2_Put_Activa_YDevuelveElItemActualizado()
    {
        using var client = ClientWith(SuperAdminToken());

        var response = await client.PutAsJsonAsync($"{BaseUrl}/vehicle_plate", new { provider = "verifik", reason = "  Kyverum caído desde las 9:10  " }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        doc.RootElement.GetProperty("kind").GetString().Should().Be("vehicle_plate");
        doc.RootElement.GetProperty("provider").GetString().Should().Be("verifik");
        doc.RootElement.GetProperty("reason").GetString().Should().Be("Kyverum caído desde las 9:10", "el motivo se guarda recortado");
        _factory.Gateway.Items["vehicle_plate"].Provider.Should().Be("verifik");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AC3_Put_SinMotivo_Returns400ConElCampo_YNadaCambia(string? motivo)
    {
        using var client = ClientWith(SuperAdminToken());

        var response = await client.PutAsJsonAsync($"{BaseUrl}/vehicle_plate", new { provider = "verifik", reason = motivo }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        doc.RootElement.GetProperty("errors").GetProperty("reason")[0].GetString().Should().Be("Escribe el motivo del cambio.");
        _factory.Gateway.Items.Should().BeEmpty();
        _factory.Gateway.Saves.Should().Be(0);
    }

    [Theory]
    [InlineData("vehicle_plate", "verifik_conductor")]
    [InlineData("conductor", "verifik")]
    [InlineData("vehicle_vin", "intempo")]
    public async Task AC4_Put_ProveedorQueNoEsDeRuntParaEseTipo_Returns400_YNadaCambia(string kind, string provider)
    {
        using var client = ClientWith(SuperAdminToken());

        var response = await client.PutAsJsonAsync($"{BaseUrl}/{kind}", new { provider, reason = "Motivo" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        doc.RootElement.GetProperty("errors").GetProperty("provider")[0].GetString().Should().Be("Ese proveedor no atiende este tipo de consulta.");
        _factory.Gateway.Saves.Should().Be(0);
    }

    [Fact]
    public async Task AC5_Put_ConProveedorNulo_ApagaLaContingencia()
    {
        _factory.Gateway.Items["conductor"] = new RuntContingencyItem("conductor", "verifik_conductor", "Caída", "Ana", DateTimeOffset.UtcNow);
        using var client = ClientWith(SuperAdminToken());

        var response = await client.PutAsJsonAsync($"{BaseUrl}/conductor", new { provider = (string?)null, reason = "Kyverum restablecido" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        doc.RootElement.GetProperty("provider").ValueKind.Should().Be(JsonValueKind.Null);
        doc.RootElement.GetProperty("reason").GetString().Should().Be("Kyverum restablecido");
    }

    [Fact]
    public async Task Put_TipoDesconocido_Returns404()
    {
        using var client = ClientWith(SuperAdminToken());

        var response = await client.PutAsJsonAsync($"{BaseUrl}/multas", new { provider = "verifik", reason = "Motivo" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AC6_SinSesion_401_YSinElPermiso_403_AunSiendoAdminCompany()
    {
        using var anonimo = _factory.CreateClient();
        (await anonimo.GetAsync(BaseUrl, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        foreach (var rol in new[] { "AdminCompany", "Operador" })
        {
            using var client = ClientWith(TokenWith(rol));
            (await client.GetAsync(BaseUrl, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden, rol);
            (await client.PutAsJsonAsync($"{BaseUrl}/vehicle_plate", new { provider = "verifik", reason = "x" }, TestContext.Current.CancellationToken))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden, rol);
        }

        _factory.Gateway.Saves.Should().Be(0);
    }

    [Fact]
    public async Task AC7_Put_QuedaEnLaAuditoriaDePlataforma_SinEmpresa_ConAnteriorYNuevo()
    {
        _factory.Gateway.Items["vehicle_plate"] = new RuntContingencyItem("vehicle_plate", "kyverum_runt", "Antes", "Ana", DateTimeOffset.UtcNow);
        using var client = ClientWith(SuperAdminToken());

        var response = await client.PutAsJsonAsync($"{BaseUrl}/vehicle_plate", new { provider = "verifik", reason = "Kyverum caído" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entry = _factory.AuditWriter.ReceivedCalls().Single().GetArguments()[0].Should().BeOfType<AdminAuditEntry>().Subject;
        entry.TenantId.Should().BeNull("la auditoría es de Plataforma: ningún historial de empresa registra nada");
        entry.ActorUserId.Should().Be(SuperAdminUserId);
        entry.EntityName.Should().Be("runt_contingency");
        entry.Result.Should().Be(AuditVocabulary.Results.Success);
        entry.OldValue.Should().Contain("\"provider\":\"kyverum_runt\"").And.Contain("\"reason\":\"Antes\"");
        using var nuevo = JsonDocument.Parse(entry.NewValue!);
        nuevo.RootElement.GetProperty("provider").GetString().Should().Be("verifik");
        nuevo.RootElement.GetProperty("reason").GetString().Should().Be("Kyverum caído");
    }

    [Fact]
    public async Task Put_ConValidacionFallida_NoAudita()
    {
        using var client = ClientWith(SuperAdminToken());

        await client.PutAsJsonAsync($"{BaseUrl}/vehicle_plate", new { provider = "verifik", reason = "" }, TestContext.Current.CancellationToken);

        _factory.AuditWriter.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Consultas_SinRespuesta_Returns503ConElMensaje()
    {
        _factory.Gateway.Unavailable = true;
        using var client = ClientWith(SuperAdminToken());

        var get = await client.GetAsync(BaseUrl, TestContext.Current.CancellationToken);
        var put = await client.PutAsJsonAsync($"{BaseUrl}/vehicle_plate", new { provider = "verifik", reason = "Motivo" }, TestContext.Current.CancellationToken);

        foreach (var response in new[] { get, put })
        {
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            doc.RootElement.GetProperty("detail").GetString().Should().Be("El servicio de consultas no responde. Intenta de nuevo en unos minutos.");
        }

        _factory.AuditWriter.ReceivedCalls().Should().BeEmpty("si no se pudo guardar no hay cambio que auditar");
    }

    private HttpClient ClientWith(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string SuperAdminToken() => TokenWith("SuperAdmin");

    /// <summary>Token con <c>role</c> y <c>role_code</c>, como los emite el emisor real (el bypass de SuperAdmin lee <c>role_code</c>).</summary>
    private static string TokenWith(string role, params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new("sub", SuperAdminUserId.ToString()),
            new("role", role),
            new("role_code", role),
        };
        claims.AddRange(permissions.Select(p => new Claim("permissions", p)));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 64))),
                SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>Puerto en memoria: guarda lo que se le pide y puede simular que core-consultas no responde.</summary>
    public sealed class FakeGateway : IRuntContingencyGateway
    {
        public Dictionary<string, RuntContingencyItem> Items { get; } = [];

        public bool Unavailable { get; set; }

        public int Saves { get; private set; }

        public void Reset()
        {
            Items.Clear();
            Unavailable = false;
            Saves = 0;
        }

        public Task<IReadOnlyList<RuntContingencyItem>> ListAsync(CancellationToken cancellationToken)
        {
            if (Unavailable)
                throw new RuntContingencyUnavailableException("caído");
            return Task.FromResult<IReadOnlyList<RuntContingencyItem>>(
                [.. RuntContingencyCatalog.Kinds.Select(k => Items.GetValueOrDefault(k) ?? new RuntContingencyItem(k, null, null, null, null))]);
        }

        public Task<RuntContingencyItem> SaveAsync(string kind, string? provider, string reason, string actorName, Guid? actorUserId, CancellationToken cancellationToken)
        {
            if (Unavailable)
                throw new RuntContingencyUnavailableException("caído");
            Saves++;
            return Task.FromResult(Items[kind] = new RuntContingencyItem(kind, provider, reason, actorName, DateTimeOffset.UtcNow));
        }
    }

    public sealed class ContingencyFactory : WebApplicationFactory<Program>
    {
        public FakeGateway Gateway { get; } = new();

        public IAdminAuditWriter AuditWriter { get; } = Substitute.For<IAdminAuditWriter>();

        public void Reset()
        {
            Gateway.Reset();
            AuditWriter.ClearReceivedCalls();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<IRuntContingencyGateway>(_ => Gateway);
                services.AddScoped(_ => AuditWriter);
            });
        }
    }
}
