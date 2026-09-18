using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Admin.Domain.Companies;
using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using Flit.Api.Authorization;
using Flit.Api.Endpoints.Tramites;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.Auditing;
using Flit.Tramites.Domain.ReadModels;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Tramites;

/// <summary>
/// HU #12652 (Feature #12257, épica #12235) — la red consolidada (<c>/api/v1/tramites/network/**</c>)
/// es exclusiva del rol AdminCompany de la cabeza. Host real sin PostgreSQL: resolver de alcance fijo
/// (P ⇒ Group CONCESION, M ⇒ Group MARCA_BLANCA, resto ⇒ Single) y repositorios de lectura
/// sustituidos para afirmar que ningún 403 ejecuta consulta alguna.
/// <list type="bullet">
///   <item>Inventario: TODAS las rutas registradas bajo el prefijo de red están en
///   <see cref="Inventory"/> (una ruta nueva sin cubrir falla nombrándola); cada teoría recorre el
///   inventario completo, así que la puerta queda probada en el 100 % de las familias.</item>
///   <item>AC2 — Radicador / Operador de la cabeza ⇒ 403 <c>network_role_required</c> en cada ruta,
///   sin tocar repositorios ni auditoría.</item>
///   <item>AC1 / AC5 — AdminCompany y multi-rol (Radicador + AdminCompany) ⇒ 200.</item>
///   <item>AC6 — hija (Single), cliente sin red y SuperAdmin ⇒ 403 <c>network_scope_required</c>
///   en cada ruta, como antes de la HU (el rol se evalúa DESPUÉS del alcance).</item>
/// </list>
/// Uso de ejemplo: <c>GET /api/v1/tramites/network/children</c> con token de P y rol Radicador ⇒
/// <c>403 { "error": "network_role_required" }</c>; con roles [Radicador, AdminCompany] ⇒ 200.
/// </summary>
public sealed class NetworkRoleGateTests : IClassFixture<NetworkRoleGateTests.GateFactory>
{
    private const string Prefix = "/api/v1/tramites/network";
    private const string Range = "from=2026-09-01&to=2026-09-14";

    /// <summary>Cabeza CONCESION con hijos C1 y C2.</summary>
    private static readonly Guid P = Guid.Parse("a0000000-0000-4000-8000-000000000100");

    /// <summary>Cabeza MARCA_BLANCA con hijo C3.</summary>
    private static readonly Guid M = Guid.Parse("a0000000-0000-4000-8000-000000000200");
    private static readonly Guid C1 = Guid.Parse("a0000000-0000-4000-8000-000000001100");
    private static readonly Guid C2 = Guid.Parse("a0000000-0000-4000-8000-000000001200");
    private static readonly Guid C3 = Guid.Parse("a0000000-0000-4000-8000-000000002100");
    private static readonly Guid S = Guid.Parse("a0000000-0000-4000-8000-000000005500");
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AnyProcedure = Guid.Parse("a0000000-0003-4000-8000-000000001101");
    private static readonly Guid AnyAttachment = Guid.Parse("a0000000-0005-4000-8000-000000000001");

    private readonly GateFactory _factory;

    public NetworkRoleGateTests(GateFactory factory)
    {
        _factory = factory;
        _factory.Hierarchy.ClearReceivedCalls();
        _factory.Instances.ClearReceivedCalls();
        _factory.Analytics.ClearReceivedCalls();
        _factory.Reports.ClearReceivedCalls();
        _factory.Writer.ClearReceivedCalls();
    }

    /// <summary>
    /// Inventario (método + plantilla) de TODAS las rutas del grupo de red: instancias, estadísticas,
    /// reportes, documentos e hijos. <see cref="El_inventario_cubre_todas_las_rutas_registradas_bajo_el_prefijo"/>
    /// lo contrasta con el <see cref="EndpointDataSource"/> real.
    /// </summary>
    public static TheoryData<string, string> Inventory => new()
    {
        { "GET", $"{Prefix}/instances" },
        { "POST", $"{Prefix}/instances/search" },
        { "POST", $"{Prefix}/instances/estado-counts" },
        { "GET", $"{Prefix}/instances/{{id:guid}}" },
        { "GET", $"{Prefix}/stats/overview" },
        { "GET", $"{Prefix}/stats/productivity/top" },
        { "GET", $"{Prefix}/stats/monthly-trend" },
        { "GET", $"{Prefix}/reports/procedures" },
        { "GET", $"{Prefix}/reports/procedures/export" },
        { "GET", $"{Prefix}/instances/{{id:guid}}/attachments" },
        { "GET", $"{Prefix}/instances/{{id:guid}}/attachments/{{attachmentId:guid}}/download" },
        { "GET", $"{Prefix}/children" },
        // HU #12708 — Validación de Identidad de la red (solo lectura).
        { "GET", $"{Prefix}/identity-validations/by-person" },
        { "GET", $"{Prefix}/identity-validations/by-person/detail" },
        { "GET", $"{Prefix}/identity-validations/{{validationId:guid}}/audit" },
    };

    // ── Inventario: 100 % de las familias de network/** bajo la misma puerta ─────────────────

    [Fact]
    public void El_inventario_cubre_todas_las_rutas_registradas_bajo_el_prefijo()
    {
        var registered = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.StartsWith(Prefix + "/", StringComparison.Ordinal))
            .SelectMany(e => e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods
                .Select(m => $"{m} {e.RoutePattern.RawText}"))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var inventory = Inventory.Select(row => $"{row.Data.Item1} {row.Data.Item2}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        registered.Should().Equal(inventory,
            "toda ruta nueva bajo /network debe entrar en el inventario para quedar cubierta por la puerta de rol");
    }

    // ── AC2 — rol no-admin de la cabeza ⇒ 403 network_role_required sin consulta ─────────────

    [Theory]
    [MemberData(nameof(Inventory))]
    public async Task Radicador_de_la_cabeza_CONCESION_403_role_required_sin_consulta(string method, string template)
    {
        var response = await Send(ClientFor(P, "Radicador"), method, template);

        await AssertForbidden(response, NetworkScopePolicy.RoleRequired);
        await AssertNothingQueried();
    }

    [Theory]
    [MemberData(nameof(Inventory))]
    public async Task Radicador_de_la_cabeza_MARCA_BLANCA_403_role_required_sin_consulta(string method, string template)
    {
        var response = await Send(ClientFor(M, "Radicador"), method, template);

        await AssertForbidden(response, NetworkScopePolicy.RoleRequired);
        await AssertNothingQueried();
    }

    [Theory]
    [InlineData("Operador")]
    [InlineData("Gestor")]
    [InlineData("ot_admin")]
    [InlineData("radicador")]
    public async Task Otros_roles_de_la_cabeza_403_role_required(string role)
    {
        var children = await Send(ClientFor(P, role), "GET", $"{Prefix}/children");
        var search = await Send(ClientFor(P, role), "POST", $"{Prefix}/instances/search");

        await AssertForbidden(children, NetworkScopePolicy.RoleRequired);
        await AssertForbidden(search, NetworkScopePolicy.RoleRequired);
        await AssertNothingQueried();
    }

    [Fact]
    public async Task Sin_claim_de_rol_403_role_required()
    {
        var response = await Send(ClientFor(P), "GET", $"{Prefix}/children");

        await AssertForbidden(response, NetworkScopePolicy.RoleRequired);
        await AssertNothingQueried();
    }

    [Fact]
    public async Task El_rol_no_se_puede_inyectar_por_cabecera_ni_query()
    {
        var client = ClientFor(P, "Radicador");
        client.DefaultRequestHeaders.Add("X-Role", "AdminCompany");

        var response = await client.GetAsync($"{Prefix}/children?role=AdminCompany&role_code=AdminCompany", TestContext.Current.CancellationToken);

        await AssertForbidden(response, NetworkScopePolicy.RoleRequired);
        await AssertNothingQueried();
    }

    // ── AC1 / AC5 — AdminCompany y multi-rol ⇒ 200 ───────────────────────────────────────────

    [Fact]
    public async Task AdminCompany_de_la_cabeza_CONCESION_200_children()
    {
        var response = await Send(ClientFor(P, "AdminCompany"), "GET", $"{Prefix}/children");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Hierarchy.Received(1).ListChildrenAsync(P, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdminCompany_de_la_cabeza_MARCA_BLANCA_200_children()
    {
        var response = await Send(ClientFor(M, "AdminCompany"), "GET", $"{Prefix}/children");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Hierarchy.Received(1).ListChildrenAsync(M, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdminCompany_200_estado_counts_y_stats_overview_ejecutan_la_consulta()
    {
        var counts = await Send(ClientFor(P, "AdminCompany"), "POST", $"{Prefix}/instances/estado-counts");
        var stats = await Send(ClientFor(P, "AdminCompany"), "GET", $"{Prefix}/stats/overview");

        counts.StatusCode.Should().Be(HttpStatusCode.OK);
        stats.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Analytics.Received(1).GetNetworkOverviewAsync(
            Arg.Is<IReadOnlySet<Guid>>(s => s.SetEquals(new[] { P, C1, C2 })), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Radicador", "AdminCompany")]
    [InlineData("AdminCompany", "Radicador")]
    [InlineData("Operador", "Gestor", "AdminCompany")]
    [InlineData("Radicador", "admincompany")]
    public async Task Multi_rol_con_AdminCompany_200_children(params string[] roles)
    {
        var response = await Send(ClientFor(P, roles), "GET", $"{Prefix}/children");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Hierarchy.Received(1).ListChildrenAsync(P, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Multi_rol_sin_AdminCompany_403_role_required()
    {
        var response = await Send(ClientFor(P, "Radicador", "Operador", "Gestor"), "GET", $"{Prefix}/children");

        await AssertForbidden(response, NetworkScopePolicy.RoleRequired);
        await AssertNothingQueried();
    }

    // ── AC6 — sin regresión: hija / cliente sin red / SuperAdmin ⇒ 403 network_scope_required ─

    [Theory]
    [MemberData(nameof(Inventory))]
    public async Task Hija_con_AdminCompany_403_scope_required_no_role(string method, string template)
    {
        var response = await Send(ClientFor(C1, "AdminCompany"), method, template);

        await AssertForbidden(response, NetworkScopePolicy.ScopeRequired);
        await AssertNothingQueried();
    }

    [Theory]
    [MemberData(nameof(Inventory))]
    public async Task Cliente_sin_red_403_scope_required_sea_cual_sea_el_rol(string method, string template)
    {
        var admin = await Send(ClientFor(S, "AdminCompany"), method, template);
        var radicador = await Send(ClientFor(S, "Radicador"), method, template);

        await AssertForbidden(admin, NetworkScopePolicy.ScopeRequired);
        await AssertForbidden(radicador, NetworkScopePolicy.ScopeRequired);
        await AssertNothingQueried();
    }

    [Theory]
    [MemberData(nameof(Inventory))]
    public async Task SuperAdmin_403_scope_required(string method, string template)
    {
        var response = await Send(ClientFor(P, "SuperAdmin"), method, template);

        await AssertForbidden(response, NetworkScopePolicy.ScopeRequired);
        await AssertNothingQueried();
    }

    [Theory]
    [MemberData(nameof(Inventory))]
    public async Task Sin_token_401(string method, string template)
    {
        var response = await Send(_factory.CreateClient(), method, template);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertNothingQueried();
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private static async Task AssertForbidden(HttpResponseMessage response, string code)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("error").GetString().Should().Be(code);
    }

    private async Task AssertNothingQueried()
    {
        await _factory.Hierarchy.DidNotReceiveWithAnyArgs().ListChildrenAsync(default, default);
        _factory.Instances.ReceivedCalls().Should().BeEmpty("el 403 de la puerta no ejecuta consulta de trámites");
        _factory.Analytics.ReceivedCalls().Should().BeEmpty("el 403 de la puerta no ejecuta consulta de estadísticas");
        _factory.Reports.ReceivedCalls().Should().BeEmpty("el 403 de la puerta no ejecuta consulta de reportes");
        await _factory.Writer.DidNotReceiveWithAnyArgs().WriteAsync(default!, default);
    }

    /// <summary>
    /// Construye la petición real a partir de la plantilla del inventario: sustituye los parámetros de
    /// ruta por GUIDs, añade el rango obligatorio de stats/reports y un cuerpo JSON vacío a los POST.
    /// </summary>
    private static Task<HttpResponseMessage> Send(HttpClient client, string method, string template)
    {
        var url = template
            .Replace("{id:guid}", AnyProcedure.ToString(), StringComparison.Ordinal)
            .Replace("{attachmentId:guid}", AnyAttachment.ToString(), StringComparison.Ordinal)
            .Replace("{validationId:guid}", AnyAttachment.ToString(), StringComparison.Ordinal);
        if (url.Contains("/stats/", StringComparison.Ordinal) || url.Contains("/reports/", StringComparison.Ordinal))
            url += $"?{Range}";

        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "POST")
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private HttpClient ClientFor(Guid tenantId, params string[] roles)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(tenantId, roles));
        return client;
    }

    /// <summary>
    /// JWT FLIT: un par de claims <c>role</c>/<c>role_code</c> por cada rol activo (multi-rol, HU #10506)
    /// y el slug «tramites.read» para que las rutas de documentos lleguen al filtro del grupo (sin él
    /// la policy de permiso responde 403 vacío antes, y eso NO es lo que se prueba aquí).
    /// </summary>
    private static string Token(Guid tenantId, string[] roles)
    {
        var claims = new List<Claim>
        {
            new("sub", UserId.ToString()),
            new("tenant_id", tenantId.ToString()),
            new("permissions", NetworkAttachmentEndpoints.ReadPermission),
        };
        foreach (var role in roles)
        {
            claims.Add(new Claim("role", role));
            claims.Add(new Claim("role_code", role));
        }

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

    /// <summary>Host con los repositorios de lectura de la red, el auditor y el resolver de alcance sustituidos (sin PostgreSQL).</summary>
    public sealed class GateFactory : WebApplicationFactory<Program>
    {
        public ICompanyHierarchyRepository Hierarchy { get; } = Substitute.For<ICompanyHierarchyRepository>();

        public IProcedureInstanceRepository Instances { get; } = Substitute.For<IProcedureInstanceRepository>();

        public INetworkAnalyticsReadRepository Analytics { get; } = Substitute.For<INetworkAnalyticsReadRepository>();

        public INetworkDetailedReportReadRepository Reports { get; } = Substitute.For<INetworkDetailedReportReadRepository>();

        public INetworkAccessAuditWriter Writer { get; } = Substitute.For<INetworkAccessAuditWriter>();

        public GateFactory()
        {
            Hierarchy.ListChildrenAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(new List<CompanyChildListItem>
                {
                    new() { Id = C1, Nit = "900000001", RazonSocial = "Alfa (C1)", Code = "C1", TenantType = "CONCESIONARIO", EstadoActivo = true },
                });
            Instances.CountByStatusFilteredAsync(Arg.Any<TenantScope>(), Arg.Any<ProcedureInstanceListFilter>(), Arg.Any<CancellationToken>())
                .Returns(new Dictionary<string, int> { ["entregado"] = 2 });
            Instances.ListTenantIdsWithMatchesAsync(Arg.Any<TenantScope>(), Arg.Any<ProcedureInstanceListFilter>(), Arg.Any<CancellationToken>())
                .Returns(new List<Guid> { C1 });
            Analytics.GetNetworkOverviewAsync(Arg.Any<IReadOnlySet<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var set = call.ArgAt<IReadOnlySet<Guid>>(0);
                    IReadOnlyList<CategoryMetricsDto> items = [];
                    return new NetworkAnalyticsResult<IReadOnlyList<CategoryMetricsDto>>(items, set.Where(t => t != P).OrderBy(t => t).ToList());
                });
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => Hierarchy);
                services.AddScoped(_ => Instances);
                services.AddScoped(_ => Analytics);
                services.AddScoped(_ => Reports);
                services.AddScoped(_ => Writer);
                services.AddScoped<ITenantScopeResolver>(_ => new FakeScopeResolver());
            });
        }
    }

    private sealed class FakeScopeResolver : ITenantScopeResolver
    {
        public Task<TenantScope> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(tenantId == P
                ? TenantScope.Group(P, [C1, C2], GroupKind.Concesion)
                : tenantId == M
                    ? TenantScope.Group(M, [C3], GroupKind.MarcaBlanca)
                    : TenantScope.Single(tenantId));
    }
}
