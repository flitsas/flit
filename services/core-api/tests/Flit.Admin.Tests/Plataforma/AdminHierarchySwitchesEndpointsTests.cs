using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Admin.Application.Auditing;
using Flit.Admin.Tests.Companies;
using Flit.Queries.Domain.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Plataforma;

/// <summary>
/// HU #12323 (Feature #12254) — <c>/api/v1/admin/platform/hierarchy-switches</c>: solo SuperAdmin;
/// GET lista, PUT conmuta (200 estado / 404 clave desconocida / 400 body inválido) y audita.
/// Uso de ejemplo: <c>PUT /api/v1/admin/platform/hierarchy-switches/group_read_scope { "isEnabled": false }</c>.
/// <see cref="IHierarchySwitches"/> e <see cref="IAdminAuditWriter"/> se sustituyen por dobles: sin PostgreSQL.
/// </summary>
public sealed class AdminHierarchySwitchesEndpointsTests : IClassFixture<AdminHierarchySwitchesEndpointsTests.SwitchesFactory>
{
    private const string BaseUrl = "/api/v1/admin/platform/hierarchy-switches";
    private static readonly Guid SuperAdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly SwitchesFactory _factory;

    public AdminHierarchySwitchesEndpointsTests(SwitchesFactory factory)
    {
        _factory = factory;
    }

    // ── Autorización ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_SinToken_Devuelve401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(BaseUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_AdminCompany_Devuelve403()
    {
        using var client = ClientAs("AdminCompany");

        var response = await client.GetAsync(BaseUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Put_AdminCompany_Devuelve403_YNoConmuta()
    {
        using var client = ClientAs("AdminCompany");
        _factory.Switches.ClearReceivedCalls();

        var response = await client.PutAsJsonAsync(
            $"{BaseUrl}/{IHierarchySwitches.GroupReadScopeKey}",
            new { isEnabled = false },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _factory.Switches.DidNotReceiveWithAnyArgs()
            .SetAsync(default!, default, default, TestContext.Current.CancellationToken);
    }

    // ── GET ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_SuperAdmin_Devuelve200ConLosDosInterruptores()
    {
        using var client = ClientAs("SuperAdmin");

        var response = await client.GetAsync(BaseUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var items = body.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(2);
        items.Select(i => i.GetProperty("key").GetString()).Should().BeEquivalentTo(
            IHierarchySwitches.GroupReadScopeKey, IHierarchySwitches.InheritedConfigurationKey);
        items.Single(i => i.GetProperty("key").GetString() == IHierarchySwitches.GroupReadScopeKey)
            .GetProperty("isEnabled").GetBoolean().Should().BeTrue();
        items.Single(i => i.GetProperty("key").GetString() == IHierarchySwitches.InheritedConfigurationKey)
            .GetProperty("isEnabled").GetBoolean().Should().BeFalse();
    }

    // ── PUT ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Put_SuperAdmin_ClaveValida_Devuelve200ConElEstadoYActor()
    {
        using var client = ClientAs("SuperAdmin");
        _factory.Switches.ClearReceivedCalls();

        var response = await client.PutAsJsonAsync(
            $"{BaseUrl}/{IHierarchySwitches.GroupReadScopeKey}",
            new { isEnabled = false },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("key").GetString().Should().Be(IHierarchySwitches.GroupReadScopeKey);
        body.GetProperty("isEnabled").GetBoolean().Should().BeFalse();
        body.GetProperty("updatedBy").GetGuid().Should().Be(SuperAdminUserId);
        await _factory.Switches.Received(1).SetAsync(
            IHierarchySwitches.GroupReadScopeKey, false, SuperAdminUserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Put_SuperAdmin_ClaveDesconocida_Devuelve404()
    {
        using var client = ClientAs("SuperAdmin");

        var response = await client.PutAsJsonAsync(
            $"{BaseUrl}/no_existe",
            new { isEnabled = true },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("code").GetString().Should().Be("HIERARCHY_SWITCH_NOT_FOUND");
    }

    [Fact]
    public async Task Put_SuperAdmin_BodySinIsEnabled_Devuelve400()
    {
        using var client = ClientAs("SuperAdmin");
        _factory.Switches.ClearReceivedCalls();

        var response = await client.PutAsJsonAsync(
            $"{BaseUrl}/{IHierarchySwitches.GroupReadScopeKey}",
            new { },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await _factory.Switches.DidNotReceiveWithAnyArgs()
            .SetAsync(default!, default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Put_SuperAdmin_BodyNoBooleano_Devuelve400()
    {
        using var client = ClientAs("SuperAdmin");

        using var content = new StringContent("{\"isEnabled\":\"apagado\"}", System.Text.Encoding.UTF8, "application/json");
        var response = await client.PutAsync(
            $"{BaseUrl}/{IHierarchySwitches.GroupReadScopeKey}",
            content,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Auditoría ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Put_SuperAdmin_DejaRastroDeAuditoriaConModuloYOperacion()
    {
        using var client = ClientAs("SuperAdmin");
        _factory.AuditWriter.ClearReceivedCalls();

        var response = await client.PutAsJsonAsync(
            $"{BaseUrl}/{IHierarchySwitches.InheritedConfigurationKey}",
            new { isEnabled = true },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.AuditWriter.Received(1).WriteAsync(
            Arg.Is<AdminAuditEntry>(e =>
                e.Module == AuditVocabulary.Modules.Config
                && e.Operation == AuditVocabulary.Operations.Update
                && e.EntityName == "hierarchy_switch"
                && e.Result == AuditVocabulary.Results.Success
                && e.ActorUserId == SuperAdminUserId),
            Arg.Any<CancellationToken>());
    }

    private HttpClient ClientAs(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken(role));
        return client;
    }

    /// <summary>Host con <see cref="IHierarchySwitches"/> e <see cref="IAdminAuditWriter"/> sustituidos por dobles.</summary>
    public sealed class SwitchesFactory : WebApplicationFactory<Program>
    {
        public IHierarchySwitches Switches { get; } = Substitute.For<IHierarchySwitches>();

        public IAdminAuditWriter AuditWriter { get; } = Substitute.For<IAdminAuditWriter>();

        public SwitchesFactory()
        {
            var now = DateTimeOffset.UtcNow;
            Switches.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<HierarchySwitchState>
            {
                new(IHierarchySwitches.GroupReadScopeKey, true, now, null),
                new(IHierarchySwitches.InheritedConfigurationKey, false, now, null),
            });
            Switches.SetAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var key = call.ArgAt<string>(0);
                    return key is IHierarchySwitches.GroupReadScopeKey or IHierarchySwitches.InheritedConfigurationKey
                        ? new HierarchySwitchState(key, call.ArgAt<bool>(1), DateTimeOffset.UtcNow, call.ArgAt<Guid?>(2))
                        : null;
                });
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => Switches);
                services.AddScoped(_ => AuditWriter);
            });
        }
    }
}
