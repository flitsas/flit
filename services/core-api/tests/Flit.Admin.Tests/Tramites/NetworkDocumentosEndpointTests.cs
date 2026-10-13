using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.Auditing;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using NSubstitute.ClearExtensions;
using Xunit;

namespace Flit.Admin.Tests.Tramites;

/// <summary>
/// Ajuste #13419 AC5 (HU #13417, épica #13216) — <c>GET /api/v1/tramites/network/documentos</c>: la vista de red
/// sabe si la cabeza puede leer documentos de su red (y, por tanto, ofrecer «Descargar ZIP») sin el endpoint de
/// interruptores del Super Admin. La decisión es la de <see cref="NetworkDocumentsPolicy"/> (la misma que el alta del
/// lote de red): MARCA_BLANCA siempre; CONCESIÓN solo con <c>network_documents_concesion</c> encendido. Hereda las
/// puertas del grupo <c>/network/**</c> (403 <c>network_scope_required</c> / <c>network_role_required</c>, 401 sin
/// token) y no escribe en <c>network_access_audit</c> (P3 = b). Host real sin PostgreSQL.
/// <para>Uso de ejemplo: <c>GET /api/v1/tramites/network/documentos</c> con token de la cabeza ⇒
/// <c>200 { "documentosRed": true }</c>.</para>
/// </summary>
public sealed class NetworkDocumentosEndpointTests : IClassFixture<NetworkDocumentosEndpointTests.DocumentosFactory>
{
    private const string Ruta = "/api/v1/tramites/network/documentos";

    private static readonly Guid Concesion = Guid.Parse("13419000-0000-4000-8000-0000000000a1");
    private static readonly Guid MarcaBlanca = Guid.Parse("13419000-0000-4000-8000-0000000000b1");
    private static readonly Guid C1 = Guid.Parse("13419000-0000-4000-8000-0000000000c1");
    private static readonly Guid C2 = Guid.Parse("13419000-0000-4000-8000-0000000000c2");
    private static readonly Guid Sola = Guid.Parse("13419000-0000-4000-8000-0000000000f5");
    private static readonly Guid UsuarioId = Guid.Parse("13419000-0000-4000-8000-000000000001");

    private readonly DocumentosFactory _factory;

    public NetworkDocumentosEndpointTests(DocumentosFactory factory)
    {
        _factory = factory;
        _factory.Reiniciar();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── 200 — la regla de NetworkDocumentsPolicy ───────────────────────────────────────────

    [Fact]
    public async Task CabezaMarcaBlanca_200_DocumentosRedTrue_SinLeerElInterruptor()
    {
        var raiz = await Ok(Cliente(MarcaBlanca, "AdminCompany"));

        raiz.GetProperty("documentosRed").GetBoolean().Should().BeTrue();
        raiz.EnumerateObject().Select(p => p.Name).Should().Equal(["documentosRed"], "el contrato es solo el booleano");
        await _factory.Switches.DidNotReceiveWithAnyArgs().IsNetworkDocumentsConcesionEnabledAsync(default);
    }

    [Fact]
    public async Task CabezaConcesion_ConInterruptorEncendido_200_DocumentosRedTrue()
    {
        _factory.DocumentosConcesion = true;

        var raiz = await Ok(Cliente(Concesion, "AdminCompany"));

        raiz.GetProperty("documentosRed").GetBoolean().Should().BeTrue();
        await _factory.Switches.Received(1).IsNetworkDocumentsConcesionEnabledAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CabezaConcesion_ConInterruptorApagado_200_DocumentosRedFalse()
    {
        _factory.DocumentosConcesion = false;

        var raiz = await Ok(Cliente(Concesion, "AdminCompany"));

        raiz.GetProperty("documentosRed").GetBoolean().Should().BeFalse("AC5: sin documentos de red no se ofrece «Descargar ZIP»");
    }

    [Fact]
    public async Task ElAlcanceNoSaleDeLaPeticion_XTenantIdDeOtraCabezaNoCambiaLaRespuesta()
    {
        _factory.DocumentosConcesion = false;
        var client = Cliente(Concesion, "AdminCompany");
        client.DefaultRequestHeaders.Add("X-Tenant-Id", MarcaBlanca.ToString());

        var raiz = await Ok(client, $"{Ruta}?tenantId={MarcaBlanca}");

        raiz.GetProperty("documentosRed").GetBoolean().Should().BeFalse("decide la cabeza del token (BD), no la petición");
    }

    // ── 403 / 401 — mismas puertas que /network/children ───────────────────────────────────

    [Theory]
    [InlineData("hija")]
    [InlineData("sola")]
    [InlineData("superadmin")]
    public async Task NoCabeza_403_NetworkScopeRequired_IgualQueChildren(string caso)
    {
        var client = caso switch
        {
            "hija" => Cliente(C1, "AdminCompany"),
            "sola" => Cliente(Sola, "AdminCompany"),
            _ => Cliente(Concesion, "SuperAdmin"),
        };

        await Prohibido(client, NetworkScopePolicy.ScopeRequired);
    }

    [Fact]
    public async Task CabezaSinRolAdmin_403_NetworkRoleRequired_IgualQueChildren()
    {
        await Prohibido(Cliente(Concesion, "Radicador"), NetworkScopePolicy.RoleRequired);
    }

    [Fact]
    public async Task SinToken_401()
    {
        var response = await _factory.CreateClient().GetAsync(Ruta, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await _factory.Switches.DidNotReceiveWithAnyArgs().IsNetworkDocumentsConcesionEnabledAsync(default);
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────────

    private async Task<JsonElement> Ok(HttpClient client, string ruta = Ruta)
    {
        var response = await client.GetAsync(ruta, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        await _factory.Writer.DidNotReceiveWithAnyArgs().WriteAsync(default!, default);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private async Task Prohibido(HttpClient client, string codigo)
    {
        var response = await client.GetAsync(Ruta, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("error").GetString().Should().Be(codigo);
        await _factory.Switches.DidNotReceiveWithAnyArgs().IsNetworkDocumentsConcesionEnabledAsync(default);
        await _factory.Writer.DidNotReceiveWithAnyArgs().WriteAsync(default!, default);
    }

    private HttpClient Cliente(Guid tenantId, string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(tenantId, role));
        return client;
    }

    private static string Token(Guid tenantId, string role)
    {
        var claims = new List<Claim>
        {
            new("sub", UsuarioId.ToString()),
            new("role", role),
            new("role_code", role),
            new("tenant_id", tenantId.ToString()),
        };

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 64))), SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>
    /// Host con el interruptor, el auditor de red y el resolver de alcance sustituidos: <see cref="Concesion"/> es grupo
    /// CONCESIÓN {C1, C2}, <see cref="MarcaBlanca"/> grupo MARCA_BLANCA; cualquier otro tenant es <c>Single</c>.
    /// </summary>
    public sealed class DocumentosFactory : WebApplicationFactory<Program>
    {
        public IHierarchySwitches Switches { get; } = Substitute.For<IHierarchySwitches>();

        public INetworkAccessAuditWriter Writer { get; } = Substitute.For<INetworkAccessAuditWriter>();

        public bool DocumentosConcesion { get; set; }

        public DocumentosFactory() => Reiniciar();

        public void Reiniciar()
        {
            Switches.ClearSubstitute();
            Writer.ClearSubstitute();
            DocumentosConcesion = false;
            Switches.IsNetworkDocumentsConcesionEnabledAsync(Arg.Any<CancellationToken>()).Returns(_ => DocumentosConcesion);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHierarchySwitches>();
                services.AddSingleton(Switches);
                services.AddScoped(_ => Writer);
                services.AddScoped<ITenantScopeResolver>(_ => new Resolver());
            });
        }
    }

    private sealed class Resolver : ITenantScopeResolver
    {
        public Task<TenantScope> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(
                tenantId == Concesion ? TenantScope.Group(Concesion, [C1, C2], GroupKind.Concesion)
                : tenantId == MarcaBlanca ? TenantScope.Group(MarcaBlanca, [C2], GroupKind.MarcaBlanca)
                : TenantScope.Single(tenantId));
    }
}
