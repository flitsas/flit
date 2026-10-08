using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Flit.Ict.Api.Authorization;
using Flit.Ict.Api.Endpoints;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Trazabilidad;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Flit.Ict.Application.Tests.Observability;

/// <summary>
/// Bug #13445, punto 4 (decisiones D10/D11) — quién abre Logs ICT y Trazabilidad ICT.
/// </summary>
/// <remarks>
/// <para>
/// Uso de ejemplo (producción): <c>app.MapIctObservabilityEndpoints(); app.MapIctTrazabilidadEndpoints();</c>
/// en <c>Program.cs</c> de core-ict. Aquí se montan esos mismos dos grupos sobre un <c>TestServer</c>
/// con las consultas sustituidas: lo que se verifica es el BORDE (permiso, rol y alcance por tenant),
/// no el SQL, así que no hace falta PostgreSQL.
/// </para>
/// <para>
/// Reglas: Logs (<c>/logs</c>, <c>/alerts</c>) solo SuperAdmin, aunque el token traiga
/// <c>ict.logs.read</c>; Trazabilidad con <c>ict.trazabilidad.read</c> (o SuperAdmin) y atada al tenant
/// del token; revelar PII exige además <c>ict.pii.reveal</c>.
/// </para>
/// </remarks>
public sealed class IctPlatformAccessEndpointsTests : IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.Parse("92569aac-ede9-48f1-9a0e-4a724bade866");

    private const string LogsUrl = "/api/v1/ict/logs";
    private const string AlertsUrl = "/api/v1/ict/alerts";
    private const string BandejaUrl = "/api/v1/ict/trazabilidad/tramites";
    private const string TiposUrl = "/api/v1/ict/trazabilidad/tipos";
    private const string RevelarUrl = "/api/v1/ict/trazabilidad/tramites/1001/datos/revelar";

    private readonly IIntegrationLogQuery _logs = Substitute.For<IIntegrationLogQuery>();
    private readonly ITrazabilidadBandejaQuery _bandeja = Substitute.For<ITrazabilidadBandejaQuery>();
    private readonly IRevelarDatosPersonalesQuery _revelar = Substitute.For<IRevelarDatosPersonalesQuery>();
    private WebApplication? _app;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(_logs);
        builder.Services.AddSingleton(Substitute.For<IIctAlertMetricsQuery>());
        builder.Services.AddSingleton(_bandeja);
        builder.Services.AddSingleton(Substitute.For<ITiposTramiteQuery>());
        builder.Services.AddSingleton(Substitute.For<IRecorridoTramiteQuery>());
        builder.Services.AddSingleton(Substitute.For<IConsultasFuenteQuery>());
        builder.Services.AddSingleton(Substitute.For<IDatosTramiteQuery>());
        builder.Services.AddSingleton(Substitute.For<ILogTramiteQuery>());
        builder.Services.AddSingleton(_revelar);

        _app = builder.Build();
        _app.MapIctObservabilityEndpoints();
        _app.MapIctTrazabilidadEndpoints();
        await _app.StartAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    // ── Logs ICT: solo SuperAdmin ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(LogsUrl)]
    [InlineData(AlertsUrl)]
    public async Task Logs_ConSuperAdmin_Devuelve200(string url)
    {
        var response = await Client(Token("SuperAdmin", null)).GetAsync(url, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(LogsUrl)]
    [InlineData(AlertsUrl)]
    public async Task Logs_ConIctLogsReadSinSuperAdmin_Devuelve403(string url)
    {
        // El slug histórico ya no abre los logs: la regla es de rol (D10).
        var response = await Client(Token("admin_tramites", TenantId, "ict.logs.read")).GetAsync(url, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _logs.DidNotReceiveWithAnyArgs().QueryAsync(default!, Ct);
    }

    [Theory]
    [InlineData(LogsUrl)]
    [InlineData(AlertsUrl)]
    public async Task Logs_ConPermisosDeTrazabilidadYReportes_Devuelve403(string url)
    {
        // Admin Company recibe trazabilidad + reportes; ninguno de los dos abre los logs.
        var response = await Client(Token("AdminCompany", TenantId, "ict.trazabilidad.read", "ict.reportes.read"))
            .GetAsync(url, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Trazabilidad ICT: ict.trazabilidad.read ───────────────────────────────────────────────

    [Theory]
    [InlineData(BandejaUrl)]
    [InlineData(TiposUrl)]
    public async Task Trazabilidad_ConIctTrazabilidadRead_Devuelve200(string url)
    {
        var response = await Client(Token("admin_tramites", TenantId, "ict.trazabilidad.read")).GetAsync(url, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Trazabilidad_SinSuperAdmin_QuedaAtadaAlTenantDelTokenAunquePidaOtraCompania()
    {
        var otra = Guid.Parse("33333333-3333-3333-3333-333333333333");

        var response = await Client(Token("admin_tramites", TenantId, "ict.trazabilidad.read"))
            .GetAsync($"{BandejaUrl}?compania={otra}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _bandeja.Received(1).ConsultarAsync(
            Arg.Is<TrazabilidadFiltro>(f => f.TenantId == TenantId && f.CompaniaTenantId == otra),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(BandejaUrl)]
    [InlineData(TiposUrl)]
    public async Task Trazabilidad_SinSlug_Devuelve403(string url)
    {
        // ict.logs.read tampoco abre la trazabilidad: cada módulo tiene su propio permiso.
        var response = await Client(Token("admin_tramites", TenantId, "ict.logs.read", "ict.reportes.read"))
            .GetAsync(url, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _bandeja.DidNotReceiveWithAnyArgs().ConsultarAsync(default!, Ct);
    }

    [Fact]
    public async Task Trazabilidad_ConSuperAdmin_VeTodosLosTenants()
    {
        var response = await Client(Token("SuperAdmin", TenantId)).GetAsync(BandejaUrl, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _bandeja.Received(1).ConsultarAsync(
            Arg.Is<TrazabilidadFiltro>(f => f.TenantId == null), Arg.Any<CancellationToken>());
    }

    // ── Revelar PII: sigue exigiendo ict.pii.reveal (D11) ─────────────────────────────────────

    [Fact]
    public async Task RevelarPii_ConTrazabilidadSinPiiReveal_Devuelve403YNoAudita()
    {
        var response = await Client(Token("admin_tramites", TenantId, "ict.trazabilidad.read"))
            .PostAsync(RevelarUrl, content: null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _revelar.DidNotReceiveWithAnyArgs().RevelarAsync(default, default, default!, Ct);
    }

    [Fact]
    public async Task RevelarPii_ConPiiRevealSinTrazabilidad_Devuelve403()
    {
        var response = await Client(Token("admin_tramites", TenantId, "ict.pii.reveal"))
            .PostAsync(RevelarUrl, content: null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RevelarPii_ConTrazabilidadYPiiReveal_PasaElBordeConSuTenant()
    {
        var response = await Client(Token("admin_tramites", TenantId, "ict.trazabilidad.read", "ict.pii.reveal"))
            .PostAsync(RevelarUrl, content: null, Ct);

        // La consulta sustituida devuelve null (trámite inexistente) → 404: lo que importa es que el
        // borde dejó pasar la petición y que la consulta recibió el tenant del token.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await _revelar.Received(1).RevelarAsync(
            1001, TenantId, Arg.Any<SolicitanteRevelado>(), Arg.Any<CancellationToken>());
    }

    // ── Fail-closed: no SuperAdmin sin tenant_id no ve nada (Bug #13445 p.4) ──────────────────

    [Fact]
    public async Task Trazabilidad_NoSuperAdminSinTenant_Devuelve403()
    {
        // Sin tenant el SQL (@tenant IS NULL OR ...) devolvería todos los tenants: se cierra en el borde.
        var response = await Client(Token("admin_tramites", null, "ict.trazabilidad.read")).GetAsync(BandejaUrl, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _bandeja.DidNotReceiveWithAnyArgs().ConsultarAsync(default!, Ct);
    }

    [Fact]
    public async Task RevelarPii_NoSuperAdminSinTenant_Devuelve403YNoAudita()
    {
        var response = await Client(Token("admin_tramites", null, "ict.trazabilidad.read", "ict.pii.reveal"))
            .PostAsync(RevelarUrl, content: null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _revelar.DidNotReceiveWithAnyArgs().RevelarAsync(default, default, default!, Ct);
    }

    // ── Contrato de PlatformAccessReader ──────────────────────────────────────────────────────

    [Fact]
    public void Reader_ConIctTrazabilidadRead_SoloAbreTrazabilidad()
    {
        var access = Read(Token("admin_tramites", TenantId, "ict.trazabilidad.read"));

        access.HasIctTrazabilidadAccess.Should().BeTrue();
        access.IsSuperAdmin.Should().BeFalse();
        access.HasPiiRevealAccess.Should().BeFalse();
        access.HasClientAdminAccess.Should().BeFalse();
        access.TenantId.Should().Be(TenantId);
    }

    [Fact]
    public void Reader_ConSuperAdmin_AbreTodo()
    {
        var access = Read(Token("SuperAdmin", null));

        access.IsSuperAdmin.Should().BeTrue();
        access.HasIctTrazabilidadAccess.Should().BeTrue();
        access.HasPiiRevealAccess.Should().BeTrue();
    }

    [Fact]
    public void Reader_SinToken_NoAbreNada()
    {
        var access = PlatformAccessReader.Read(new DefaultHttpContext());

        access.IsSuperAdmin.Should().BeFalse();
        access.HasIctTrazabilidadAccess.Should().BeFalse();
        access.HasPiiRevealAccess.Should().BeFalse();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    private HttpClient Client(string token)
    {
        var client = _app!.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static PlatformAccess Read(string token)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {token}";
        return PlatformAccessReader.Read(context);
    }

    /// <summary>JWT de plataforma sin firmar: core-ict solo lo decodifica (el Gateway ya aplicó JwtRequired).</summary>
    private static string Token(string role, Guid? tenantId, params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new("sub", "11111111-1111-1111-1111-111111111111"),
            new("role", role),
            new("role_code", role),
        };
        if (tenantId is { } tenant)
        {
            claims.Add(new Claim("tenant_id", tenant.ToString()));
        }

        claims.AddRange(permissions.Select(p => new Claim("permissions", p)));

        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1)));
    }
}
