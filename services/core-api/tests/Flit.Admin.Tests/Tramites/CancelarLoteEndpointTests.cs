using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
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
/// HU #13385 (Feature #13307, diseño 09 §2.4/§4) — contrato HTTP de <c>POST /api/v1/consolidados/lotes/{loteId}/cancelacion</c>
/// con el host real y el repositorio del lote como doble (la transacción la cubre la integración con PostgreSQL):
/// <list type="bullet">
///   <item>202 <c>LoteConsolidados</c> en <c>cancelado</c> (también idempotente), 404 a no dueños, 409
///   <c>{error: lote_terminado, estado}</c>, 503 <c>auditoria_no_registrada</c>.</item>
///   <item>AC7: sin <c>consolidado-masivo.download</c> → 403 por <c>RequirePermission</c> y el repositorio no se invoca.</item>
///   <item>Ruta neutra: el dueño es el <c>sub</c> (se ignora <c>X-Tenant-Id</c>); el <c>ot_admin</c> (tenant OT) cancela su
///   lote OT y el Super Admin el suyo.</item>
/// </list>
/// <para>Uso de ejemplo: <c>POST /api/v1/consolidados/lotes/{id}/cancelacion</c> con el token del dueño ⇒ 202.</para>
/// </summary>
public sealed class CancelarLoteEndpointTests : IClassFixture<CancelarLoteEndpointTests.Factory>
{
    private const string Rutas = "/api/v1/consolidados/lotes";
    private const string Permiso = "consolidado-masivo.download";

    private static readonly Guid TenantC = Guid.Parse("c0000000-0000-4000-8000-0000000133c5");
    private static readonly Guid TenantB = Guid.Parse("c0000000-0000-4000-8000-0000000133b5");
    private static readonly Guid TenantOt = Guid.Parse("e1338500-0000-4000-8000-0000000000a1");
    private static readonly Guid Dueno = Guid.Parse("13385000-0000-4000-8000-000000000001");
    private static readonly Guid SuperAdminId = Guid.Parse("13385000-0000-4000-8000-000000000003");
    private static readonly Guid OtAdminId = Guid.Parse("13385000-0000-4000-8000-000000000004");
    private static readonly DateTimeOffset Cancelado = new(2026, 10, 7, 21, 15, 0, TimeSpan.Zero);

    private readonly Factory _factory;

    public CancelarLoteEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reiniciar();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AC1_Dueno_202_LoteCanceladoConContadoresYTerminadoEn_YElSubDelToken()
    {
        var lote = LoteCancelado(Dueno);
        Responde(new CancelarLoteResultado(CancelarLoteEstado.Cancelado, lote, 5, ["fm/parte-1"]));

        var client = Cliente(Token(Dueno, TenantC, "Radicador"));
        client.DefaultRequestHeaders.Add("X-Tenant-Id", TenantB.ToString());
        var response = await client.PostAsync($"{Rutas}/{lote.Id}/cancelacion", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        response.Headers.Location!.ToString().Should().Be($"{Rutas}/{lote.Id}");
        var raiz = await Raiz(response);
        raiz.GetProperty("id").GetGuid().Should().Be(lote.Id);
        raiz.GetProperty("estado").GetString().Should().Be("cancelado");
        raiz.GetProperty("total").GetInt32().Should().Be(10);
        raiz.GetProperty("procesados").GetInt32().Should().Be(5);
        raiz.GetProperty("incluidos").GetInt32().Should().Be(4);
        raiz.GetProperty("omitidos").GetInt32().Should().Be(1);
        raiz.GetProperty("generados").GetInt32().Should().Be(3);
        raiz.GetProperty("terminadoEn").GetDateTimeOffset().Should().Be(Cancelado);
        raiz.GetProperty("expiraEn").GetDateTimeOffset().Should().Be(Cancelado);
        raiz.GetProperty("partes").GetArrayLength().Should().Be(0);

        await _factory.Repo.Received(1).CancelarAsync(
            Arg.Is<CancelacionLote>(c => c.LoteId == lote.Id && c.UsuarioId == Dueno && c.RolCodigo == "Radicador"
                                         && c.UserAgent == "flit-tests/13385"),
            Arg.Any<CancellationToken>());
        _factory.Storage.Received(1).Delete("fm/parte-1");
    }

    [Fact]
    public async Task AC4_YaCancelado_202_TalCual()
    {
        var lote = LoteCancelado(Dueno);
        Responde(new CancelarLoteResultado(CancelarLoteEstado.YaCancelado, lote));

        var response = await Cliente(Token(Dueno, TenantC, "Radicador")).PostAsync($"{Rutas}/{lote.Id}/cancelacion", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await Raiz(response)).GetProperty("estado").GetString().Should().Be("cancelado");
        _factory.Storage.DidNotReceiveWithAnyArgs().Delete(default!);
    }

    [Theory]
    [InlineData(ConsolidadoExportStatus.Completado)]
    [InlineData(ConsolidadoExportStatus.CompletadoConOmitidos)]
    [InlineData(ConsolidadoExportStatus.Fallido)]
    [InlineData(ConsolidadoExportStatus.Expirado)]
    public async Task AC5_LoteTerminado_409_LoteTerminadoConEstado(string estado)
    {
        var lote = LoteCancelado(Dueno);
        lote.Status = estado;
        Responde(new CancelarLoteResultado(CancelarLoteEstado.Terminado, lote));

        var response = await Cliente(Token(Dueno, TenantC, "Radicador")).PostAsync($"{Rutas}/{lote.Id}/cancelacion", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var raiz = await Raiz(response);
        raiz.GetProperty("error").GetString().Should().Be("lote_terminado");
        raiz.GetProperty("estado").GetString().Should().Be(estado);
    }

    [Theory]
    [InlineData("otro")]
    [InlineData("superadmin")]
    [InlineData("inexistente")]
    public async Task AC6_NoDuenoSuperAdminOInexistente_404(string quien)
    {
        Responde(new CancelarLoteResultado(CancelarLoteEstado.NoEncontrado));
        var (sub, token) = quien switch
        {
            "superadmin" => (SuperAdminId, Token(SuperAdminId, TenantC, "SuperAdmin", permisos: [])),
            "otro" => (Guid.Parse("13385000-0000-4000-8000-000000000002"),
                Token(Guid.Parse("13385000-0000-4000-8000-000000000002"), TenantC, "Radicador")),
            _ => (Dueno, Token(Dueno, TenantC, "Radicador")),
        };

        var response = await Cliente(token).PostAsync($"{Rutas}/{Guid.NewGuid()}/cancelacion", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await _factory.Repo.Received(1).CancelarAsync(
            Arg.Is<CancelacionLote>(c => c.UsuarioId == sub), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC7_SinPermiso_403_YNoSeInvocaLaCancelacion()
    {
        var response = await Cliente(Token(Dueno, TenantC, "Radicador", permisos: []))
            .PostAsync($"{Rutas}/{Guid.NewGuid()}/cancelacion", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _factory.Repo.DidNotReceiveWithAnyArgs().CancelarAsync(default!, default);
    }

    [Fact]
    public async Task AC8_AuditoriaNoRegistrada_503()
    {
        Responde(new CancelarLoteResultado(CancelarLoteEstado.NoRegistrado));

        var response = await Cliente(Token(Dueno, TenantC, "Radicador")).PostAsync($"{Rutas}/{Guid.NewGuid()}/cancelacion", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Raiz(response)).GetProperty("error").GetString().Should().Be("auditoria_no_registrada");
    }

    [Fact]
    public async Task AC9_SuperAdmin_CancelaSuLote_202_ConRolSuperAdmin()
    {
        var lote = LoteCancelado(SuperAdminId);
        lote.TenantId = null;
        lote.Origin = ConsolidadoExportOrigin.Superadmin;
        Responde(new CancelarLoteResultado(CancelarLoteEstado.Cancelado, lote, 2, []));

        var response = await Cliente(Token(SuperAdminId, TenantC, "SuperAdmin", permisos: []))
            .PostAsync($"{Rutas}/{lote.Id}/cancelacion", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        await _factory.Repo.Received(1).CancelarAsync(
            Arg.Is<CancelacionLote>(c => c.UsuarioId == SuperAdminId && c.RolCodigo == "SuperAdmin"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HU13392_OtAdminConTenantOt_CancelaSuLoteOt_202()
    {
        var lote = LoteCancelado(OtAdminId);
        lote.TenantId = TenantOt;
        lote.Origin = ConsolidadoExportOrigin.OtBandeja;
        lote.OtTransitOfficeId = Guid.NewGuid();
        lote.DocumentType = ConsolidadoExportDocumentType.ConsolidadoMaestro;
        Responde(new CancelarLoteResultado(CancelarLoteEstado.Cancelado, lote, 1, []));

        var response = await Cliente(Token(OtAdminId, TenantOt, "ot_admin")).PostAsync($"{Rutas}/{lote.Id}/cancelacion", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        (await Raiz(response)).GetProperty("tipoDocumento").GetString().Should().Be("consolidado_maestro");
        await _factory.Repo.Received(1).CancelarAsync(
            Arg.Is<CancelacionLote>(c => c.UsuarioId == OtAdminId && c.LoteId == lote.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Contrato_SinToken_401_YLaRutaVieja_Cancelar_NoExiste()
    {
        var anonimo = _factory.CreateClient();
        (await anonimo.PostAsync($"{Rutas}/{Guid.NewGuid()}/cancelacion", null, Ct)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

        var vieja = await Cliente(Token(Dueno, TenantC, "Radicador")).PostAsync($"{Rutas}/{Guid.NewGuid()}/cancelar", null, Ct);
        vieja.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        await _factory.Repo.DidNotReceiveWithAnyArgs().CancelarAsync(default!, default);
    }

    private void Responde(CancelarLoteResultado r) =>
        _factory.Repo.CancelarAsync(Arg.Any<CancelacionLote>(), Arg.Any<CancellationToken>()).Returns(r);

    private static ConsolidadoExportBatch LoteCancelado(Guid dueno) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = TenantC,
        RequestedByUserId = dueno,
        RequestedRoleCode = "Radicador",
        Origin = ConsolidadoExportOrigin.Tramites,
        DocumentType = ConsolidadoExportDocumentType.Consolidado,
        Status = ConsolidadoExportStatus.Cancelado,
        TotalItems = 10,
        IncludedCount = 4,
        OmittedCount = 1,
        GeneratedCount = 3,
        CreatedAt = Cancelado.AddMinutes(-5),
        FinishedAt = Cancelado,
        ExpiresAt = Cancelado,
        PurgedAt = Cancelado,
    };

    private HttpClient Cliente(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("flit-tests/13385");
        return client;
    }

    private static async Task<JsonElement> Raiz(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();

    private static string Token(Guid sub, Guid tenantId, string role, string[]? permisos = null)
    {
        var claims = new List<Claim>
        {
            new("sub", sub.ToString()),
            new("role", role),
            new("role_code", role),
            new("tenant_id", tenantId.ToString()),
        };
        claims.AddRange((permisos ?? [Permiso]).Select(p => new Claim("permissions", p)));

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

    /// <summary>Host con el repositorio del lote y el almacenamiento de partes sustituidos.</summary>
    public sealed class Factory : WebApplicationFactory<Program>
    {
        public IConsolidadoLoteRepository Repo { get; } = Substitute.For<IConsolidadoLoteRepository>();
        public IConsolidadoLoteParteStorage Storage { get; } = Substitute.For<IConsolidadoLoteParteStorage>();

        public void Reiniciar()
        {
            Repo.ClearSubstitute();
            Storage.ClearSubstitute();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IConsolidadoLoteRepository>();
                services.RemoveAll<IConsolidadoLoteParteStorage>();
                services.AddScoped(_ => Repo);
                services.AddScoped(_ => Storage);
            });
        }
    }
}
