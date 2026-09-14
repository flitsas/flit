using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Api.Endpoints.Auditing;
using Flit.Api.Middleware;
using Flit.Queries.Domain;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.Auditing;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Tramites;

/// <summary>
/// HU #12361 (Feature #12257) — capa HTTP de la auditoría del acceso consolidado, sin PostgreSQL:
/// <list type="bullet">
///   <item>Autorización de las dos rutas de consulta: <c>/admin/platform/network-access-audit</c> solo
///   SuperAdmin (AC4); <c>/tramites/network-access-audit/mine</c> para el cliente hijo con el tenant
///   impuesto por el middleware (AC2/AC7), nunca el <c>X-Tenant-Id</c> del caller.</item>
///   <item>Paginación acotada en servidor (<see cref="NetworkAccessAuditQuery.MaxTake"/>).</item>
///   <item><see cref="NetworkAccessAuditFilter"/>: un registro por petición solo cuando hay hijos
///   alcanzados (AC1/AC5/AC8), intento rechazado (AC7), cliente sin red no deja rastro (AC6) y
///   best-effort (un writer que lanza no rompe la respuesta).</item>
///   <item><see cref="NetworkAccessAuditContext.FiltersJson"/>: lista blanca sin placa, documento ni nombre (AC8).</item>
/// </list>
/// Uso de ejemplo: <c>GET /api/v1/admin/platform/network-access-audit?tenantId={C1}&amp;take=500</c> con
/// token SuperAdmin ⇒ 200 y <c>take</c> efectivo 200.
/// </summary>
public sealed class NetworkAccessAuditEndpointsTests : IClassFixture<NetworkAccessAuditEndpointsTests.AuditFactory>
{
    private const string AdminUrl = "/api/v1/admin/platform/network-access-audit";
    private const string MineUrl = "/api/v1/tramites/network-access-audit/mine";

    private static readonly Guid P = Guid.Parse("a0000000-0000-4000-8000-000000000100");
    private static readonly Guid C1 = Guid.Parse("a0000000-0000-4000-8000-000000001100");
    private static readonly Guid C2 = Guid.Parse("a0000000-0000-4000-8000-000000001200");
    private static readonly Guid X = Guid.Parse("a0000000-0000-4000-8000-000000009900");
    private static readonly Guid S = Guid.Parse("a0000000-0000-4000-8000-000000005500");
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly AuditFactory _factory;

    public NetworkAccessAuditEndpointsTests(AuditFactory factory)
    {
        _factory = factory;
        _factory.Reader.ClearReceivedCalls();
    }

    // ── AC4 — consulta del SuperAdmin ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Admin_Sin_token_401()
    {
        var response = await _factory.CreateClient().GetAsync(AdminUrl, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_Cabeza_de_grupo_403_solo_SuperAdmin()
    {
        var response = await ClientFor(P, "AdminCompany").GetAsync($"{AdminUrl}?tenantId={C1}", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _factory.Reader.DidNotReceive().SearchAsync(Arg.Any<NetworkAccessAuditQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Admin_SuperAdmin_200_con_filtros_y_paginacion_acotada_y_sin_PII()
    {
        var response = await ClientFor(P, "SuperAdmin").GetAsync(
            $"{AdminUrl}?tenantId={C1}&resource=network.instances.detail&page=3&take=500", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Reader.Received(1).SearchAsync(
            Arg.Is<NetworkAccessAuditQuery>(q => q.TenantId == C1 && q.Resource == "network.instances.detail"
                && q.EffectivePage == 3 && q.EffectiveTake == NetworkAccessAuditQuery.MaxTake),
            Arg.Any<CancellationToken>());

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("total").GetInt32().Should().Be(1);
        body.RootElement.GetProperty("take").GetInt32().Should().Be(NetworkAccessAuditQuery.MaxTake);
        var item = body.RootElement.GetProperty("items")[0];
        item.GetProperty("actorTenantId").GetGuid().Should().Be(P);
        item.GetProperty("reachedTenantIds").EnumerateArray().Select(e => e.GetGuid()).Should().Equal(C1);
        item.GetProperty("resource").GetString().Should().Be("network.instances.detail");
        item.GetProperty("result").GetString().Should().Be("ok");
        // Solo identificadores: ninguna propiedad con nombre de persona, documento, placa o filtros.
        item.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "id", "occurredAt", "actorUserId", "actorTenantId", "reachedTenantIds", "resource", "procedureId", "procedureTenantId", "attachmentId", "result");
    }

    // ── AC2 / AC7 — consulta del hijo ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Mine_Sin_token_401()
    {
        var response = await _factory.CreateClient().GetAsync(MineUrl, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Mine_Hijo_200_y_el_tenant_sale_del_token_no_del_header()
    {
        var client = ClientFor(C1, "AdminCompany");
        client.DefaultRequestHeaders.Add("X-Tenant-Id", C2.ToString()); // intento de leer la auditoría del hermano

        var response = await client.GetAsync($"{MineUrl}?from=2026-01-01T00:00:00Z&take=0", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Reader.Received(1).SearchAsync(
            Arg.Is<NetworkAccessAuditQuery>(q => q.TenantId == C1 && q.From != null && q.EffectiveTake == NetworkAccessAuditQuery.DefaultTake),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Mine_SuperAdmin_sin_tenant_seleccionado_403_tenant_required()
    {
        var response = await ClientFor(P, "SuperAdmin").GetAsync(MineUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("tenant_required");
    }

    [Fact]
    public void Mine_esta_declarada_como_ruta_runtime_tenant_scoped()
    {
        TenantEnforcementMiddleware.IsRuntimeScoped(MineUrl).Should().BeTrue("el tenant debe salir del middleware");
        TenantEnforcementMiddleware.IsRuntimeScoped("/api/v1/tramites/network/instances").Should().BeTrue();
    }

    // ── Endpoint filter: un registro por petición ─────────────────────────────────────────────

    [Fact]
    public async Task Filter_Cabeza_con_hijos_alcanzados_escribe_un_registro_con_los_hijos_distintos_del_alcance()
    {
        var writer = Substitute.For<INetworkAccessAuditWriter>();
        var http = HttpContextFor(TenantScope.Group(P, [C1, C2], GroupKind.Concesion), writer);
        var outcome = new NetworkAccessOutcome(
            NetworkAccessVocabulary.Resources.InstancesSearch, NetworkAccessVocabulary.Results.Ok,
            [P, C1, C1, C2, X, Guid.Empty], FiltersJson: "{\"take\":50}");

        var result = await Invoke(http, () => { NetworkAccessAuditContext.Publish(http, outcome); return Results.Ok(); });

        (result as IStatusCodeHttpResult)!.StatusCode.Should().Be(200);
        await writer.Received(1).WriteAsync(
            Arg.Is<NetworkAccessAuditEntry>(e => e.ActorTenantId == P && e.ActorUserId == UserId
                && e.ReachedTenantIds.Count == 2 && e.ReachedTenantIds.Contains(C1) && e.ReachedTenantIds.Contains(C2)
                && e.Resource == NetworkAccessVocabulary.Resources.InstancesSearch && e.Result == NetworkAccessVocabulary.Results.Ok
                && e.FiltersJson == "{\"take\":50}"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Filter_Solo_datos_propios_o_sin_desenlace_no_escribe_nada_AC5()
    {
        var writer = Substitute.For<INetworkAccessAuditWriter>();
        var scope = TenantScope.Group(P, [C1, C2], GroupKind.Concesion);

        var soloPropios = HttpContextFor(scope, writer);
        await Invoke(soloPropios, () =>
        {
            NetworkAccessAuditContext.Publish(soloPropios, new NetworkAccessOutcome(
                NetworkAccessVocabulary.Resources.InstancesSearch, NetworkAccessVocabulary.Results.Ok, [P, P]));
            return Results.Ok();
        });

        var sinDesenlace = HttpContextFor(scope, writer);
        await Invoke(sinDesenlace, () => Results.NotFound());

        await writer.DidNotReceive().WriteAsync(Arg.Any<NetworkAccessAuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Filter_Cliente_sin_red_o_SuperAdmin_no_deja_rastro_AC6()
    {
        var writer = Substitute.For<INetworkAccessAuditWriter>();
        foreach (var scope in new[] { TenantScope.Single(S), TenantScope.All() })
        {
            var http = HttpContextFor(scope, writer);
            await Invoke(http, () =>
            {
                NetworkAccessAuditContext.Publish(http, new NetworkAccessOutcome(
                    NetworkAccessVocabulary.Resources.InstancesSearch, NetworkAccessVocabulary.Results.Ok, [C1, C2]));
                return Results.Ok();
            });
        }

        var sinScope = HttpContextFor(null, writer);
        await Invoke(sinScope, () => Results.Ok());

        await writer.DidNotReceive().WriteAsync(Arg.Any<NetworkAccessAuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Filter_Rechazo_por_hijo_fuera_del_alcance_registra_el_intento_como_forbidden_AC7()
    {
        var writer = Substitute.For<INetworkAccessAuditWriter>();
        var http = HttpContextFor(TenantScope.Group(P, [C1], GroupKind.Concesion), writer);

        var result = await Invoke(http, () =>
        {
            NetworkAccessAuditContext.Publish(http, new NetworkAccessOutcome(
                NetworkAccessVocabulary.Resources.InstancesSearch, NetworkAccessVocabulary.Results.Forbidden, [X]));
            return Results.Json(new { error = NetworkScopePolicy.ChildOutOfScope }, statusCode: 403);
        });

        (result as IStatusCodeHttpResult)!.StatusCode.Should().Be(403, "la respuesta no cambia");
        await writer.Received(1).WriteAsync(
            Arg.Is<NetworkAccessAuditEntry>(e => e.Result == NetworkAccessVocabulary.Results.Forbidden
                && e.ReachedTenantIds.Single() == X && e.ActorTenantId == P),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Filter_Un_writer_que_lanza_no_rompe_la_respuesta()
    {
        var writer = Substitute.For<INetworkAccessAuditWriter>();
        writer.WriteAsync(Arg.Any<NetworkAccessAuditEntry>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("boom"));
        var http = HttpContextFor(TenantScope.Group(P, [C1], GroupKind.Concesion), writer);

        var result = await Invoke(http, () =>
        {
            NetworkAccessAuditContext.Publish(http, new NetworkAccessOutcome(
                NetworkAccessVocabulary.Resources.InstancesDetail, NetworkAccessVocabulary.Results.Ok, [C1],
                ProcedureId: Guid.NewGuid(), ProcedureTenantId: C1));
            return Results.Ok(new { ok = true });
        });

        (result as IStatusCodeHttpResult)!.StatusCode.Should().Be(200);
    }

    // ── AC8 — filtros sin datos personales ────────────────────────────────────────────────────

    [Fact]
    public void FiltersJson_lista_blanca_sin_placa_documento_ni_nombre()
    {
        var request = new ProcedureInstanceListRequest
        {
            Skip = 50,
            Take = 50,
            Placa = "ABC123",
            Vin = "1HGCM82633A004352",
            Vendedor = "Juan Pérez",
            Comprador = "María Gómez",
            Gestor = "Pedro",
            Busqueda = "1234567890",
            Estados = ["borrador", "entregado"],
            Modalidad = "virtual",
            OrganismoTransito = "BOGOTA",
            TipoCodigo = "MATRICULA_NUEVA",
            Firmado = true,
            Prioritario = false,
            CreatedFrom = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            SortBy = "createdAt",
            SortDescending = false,
            Condiciones = [new QueryCondition("placa", "es", ["ABC123"]), new QueryCondition("estado", "es_alguno", ["borrador"])],
        };

        var json = NetworkAccessAuditContext.FiltersJson(request, C1)!;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("childTenantId").GetGuid().Should().Be(C1);
        root.GetProperty("estados").EnumerateArray().Select(e => e.GetString()).Should().Equal("borrador", "entregado");
        root.GetProperty("modalidad").GetString().Should().Be("virtual");
        root.GetProperty("organismoTransito").GetString().Should().Be("BOGOTA");
        root.GetProperty("tipoCodigo").GetString().Should().Be("MATRICULA_NUEVA");
        root.GetProperty("firmado").GetBoolean().Should().BeTrue();
        root.GetProperty("skip").GetInt32().Should().Be(50);
        root.GetProperty("sortDir").GetString().Should().Be("asc");
        root.GetProperty("textFilters").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("placa", "vin", "vendedor", "comprador", "gestor", "busqueda");
        root.GetProperty("conditionFields").EnumerateArray().Select(e => e.GetString()).Should().Equal("placa", "estado");
        root.TryGetProperty("updatedFrom", out _).Should().BeFalse("los nulos no se serializan");

        foreach (var pii in new[] { "ABC123", "1HGCM82633A004352", "Juan", "Pérez", "María", "Gómez", "Pedro", "1234567890" })
            json.Should().NotContain(pii, "solo identificadores y valores de filtro, nunca datos personales");
    }

    [Fact]
    public void FiltersJson_sin_filtros_solo_paginacion_y_orden()
    {
        var json = NetworkAccessAuditContext.FiltersJson(new ProcedureInstanceListRequest(), null)!;
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("sortDir", "skip", "take");
    }

    [Fact]
    public void Policy_ReachedChildren_distintos_sin_cabeza_y_sin_ajenos_en_ok()
    {
        var scope = TenantScope.Group(P, [C1, C2], GroupKind.Concesion);
        NetworkAccessAuditPolicy.ReachedChildren(scope, Enumerable.Repeat(C1, 300).Concat([P, X, C2]), "ok")
            .Should().BeEquivalentTo([C1, C2]);
        NetworkAccessAuditPolicy.ReachedChildren(scope, [X], "forbidden").Should().Equal(X);
        NetworkAccessAuditPolicy.ReachedChildren(scope, [P], "ok").Should().BeEmpty();
        NetworkAccessAuditPolicy.ReachedChildren(TenantScope.Single(S), [C1], "ok").Should().BeEmpty();
        NetworkAccessAuditPolicy.ReachedChildren(null, [C1], "ok").Should().BeEmpty();
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private static DefaultHttpContext HttpContextFor(TenantScope? scope, INetworkAccessAuditWriter writer)
    {
        var services = new ServiceCollection();
        services.AddSingleton(writer);
        services.AddLogging();
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", UserId.ToString())], "test"));
        if (scope is not null)
            http.Items[TenantEnforcementMiddleware.TenantScopeItemKey] = scope;
        return http;
    }

    private static async Task<object?> Invoke(HttpContext http, Func<object?> endpoint)
    {
        var filter = new NetworkAccessAuditFilter();
        var context = new DefaultEndpointFilterInvocationContext(http);
        return await filter.InvokeAsync(context, _ => ValueTask.FromResult(endpoint()));
    }

    private HttpClient ClientFor(Guid tenantId, string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(tenantId, role));
        return client;
    }

    private static string Token(Guid tenantId, string role)
    {
        var claims = new List<Claim>
        {
            new("sub", UserId.ToString()),
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

    /// <summary>Host con el lector sustituido (sin PostgreSQL) y alcance de grupo para P.</summary>
    public sealed class AuditFactory : WebApplicationFactory<Program>
    {
        public INetworkAccessAuditReader Reader { get; } = Substitute.For<INetworkAccessAuditReader>();

        public AuditFactory()
        {
            Reader.SearchAsync(Arg.Any<NetworkAccessAuditQuery>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var q = call.ArgAt<NetworkAccessAuditQuery>(0);
                    IReadOnlyList<NetworkAccessAuditRow> items =
                    [
                        new(Guid.NewGuid(), DateTimeOffset.UtcNow, UserId, P, [C1], q.Resource ?? "network.instances.search",
                            Guid.NewGuid(), C1, null, "ok"),
                    ];
                    return (items, 1);
                });
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => Reader);
                services.AddScoped<ITenantScopeResolver>(_ => new FakeScopeResolver());
            });
        }
    }

    private sealed class FakeScopeResolver : ITenantScopeResolver
    {
        public Task<TenantScope> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(tenantId == P
                ? TenantScope.Group(P, [C1, C2], GroupKind.Concesion)
                : TenantScope.Single(tenantId));
    }
}
