using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.ReadModels;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Tramites;

/// <summary>
/// HU #12706 — capa HTTP de la vista «todas las compañías» de Validación de Identidad, con el
/// <c>TenantEnforcementMiddleware</c> real y los repositorios sustituidos para capturar el
/// <see cref="TenantScope"/> que llega al repositorio:
/// <list type="bullet">
///   <item>AC1/AC4 — el SuperAdmin sin <c>X-Tenant-Id</c> lee con <c>TenantScope.All</c> (por persona,
///   plano y atascadas), y cada fila trae <c>tenantId</c>/<c>tenantName</c>.</item>
///   <item>AC2 — el SuperAdmin con <c>X-Tenant-Id</c> lee solo esa compañía.</item>
///   <item>AC5 — el Administrador de Compañía nunca obtiene «todas»: sin header o con el de otra
///   compañía, lee la suya (el middleware impone la del JWT).</item>
///   <item>AC6 — la cabeza de red, por estas rutas, lee solo la suya (la red va por <c>/network</c>).</item>
///   <item>AC7 — las rutas de un registro y de escritura siguen exigiendo compañía (400).</item>
///   <item>AC8 — un registro de otra compañía por id responde 404.</item>
/// </list>
/// </summary>
public sealed class IdentityValidationScopeEndpointsTests
    : IClassFixture<IdentityValidationScopeEndpointsTests.IdentityFactory>
{
    private const string ByPersonUrl = "/api/v1/tramites/biometric-validations/by-person";
    private const string FlatUrl = "/api/v1/tramites/biometric-validations";
    private const string StuckUrl = "/api/v1/tramites/identity-validation/stuck";

    private static readonly Guid A = Guid.Parse("b0000000-0000-4000-8000-00000000000a");
    private static readonly Guid B = Guid.Parse("b0000000-0000-4000-8000-00000000000b");
    private static readonly Guid Cabeza = Guid.Parse("b0000000-0000-4000-8000-000000000100");
    private static readonly Guid Hija = Guid.Parse("b0000000-0000-4000-8000-000000001100");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly IdentityFactory _factory;

    public IdentityValidationScopeEndpointsTests(IdentityFactory factory)
    {
        _factory = factory;
        _factory.Repo.ClearReceivedCalls();
        _factory.Outbox.ClearReceivedCalls();
    }

    // ── AC1 — SuperAdmin sin compañía ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SuperAdmin_sin_header_lee_por_persona_todas_las_companias_con_su_nombre()
    {
        var response = await ClientFor(A, "SuperAdmin").GetAsync(ByPersonUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Repo.Received(1).ListBiometricValidationsGroupedByPersonAsync(
            Arg.Is<TenantScope>(s => s.IsAll), Arg.Is(0), Arg.Any<int>(), Arg.Any<BiometricPersonGroupFilter?>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _factory.Repo.Received(1).CountBiometricPersonsByEstadoAsync(
            Arg.Is<TenantScope>(s => s.IsAll), Arg.Any<BiometricPersonGroupFilter?>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var persons = body.RootElement.GetProperty("persons").EnumerateArray().ToList();
        persons.Select(p => (p.GetProperty("tenantId").GetGuid(), p.GetProperty("tenantName").GetString()))
            .Should().BeEquivalentTo([(A, "Compañía A"), (B, "Compañía B")]);
    }

    [Fact]
    public async Task SuperAdmin_sin_header_lee_el_listado_plano_y_las_atascadas_de_todas()
    {
        var client = ClientFor(A, "SuperAdmin");

        (await client.GetAsync(FlatUrl, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync(StuckUrl, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        await _factory.Repo.Received(1).ListBiometricValidationsByTenantAsync(
            Arg.Is<TenantScope>(s => s.IsAll), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<BiometricValidationListFilter?>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _factory.Repo.Received(1).CountBiometricValidationsByEstadoAsync(
            Arg.Is<TenantScope>(s => s.IsAll), Arg.Any<BiometricValidationListFilter?>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _factory.Outbox.Received(1).ListStuckAsync(
            Arg.Is<TenantScope>(s => s.IsAll), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ── AC2 — SuperAdmin acota a una compañía ──────────────────────────────────────────────────

    [Fact]
    public async Task SuperAdmin_con_header_lee_solo_esa_compania()
    {
        var client = ClientFor(A, "SuperAdmin");
        client.DefaultRequestHeaders.Add("X-Tenant-Id", B.ToString());

        var response = await client.GetAsync(ByPersonUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Repo.Received(1).ListBiometricValidationsGroupedByPersonAsync(
            Arg.Is<TenantScope>(s => IsOnly(s, B)), Arg.Is(0), Arg.Any<int>(), Arg.Any<BiometricPersonGroupFilter?>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    // ── AC5 — el Administrador de Compañía nunca obtiene «todas» ───────────────────────────────

    [Theory]
    [InlineData(ByPersonUrl)]
    [InlineData(FlatUrl)]
    [InlineData(StuckUrl)]
    public async Task AdminCompany_sin_header_lee_solo_la_suya(string url)
    {
        var response = await ClientFor(A, "AdminCompany").GetAsync(url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertNuncaTodasYSolo(A);
    }

    [Theory]
    [InlineData(ByPersonUrl)]
    [InlineData(FlatUrl)]
    [InlineData(StuckUrl)]
    public async Task AdminCompany_con_el_header_de_otra_compania_lee_solo_la_suya(string url)
    {
        var client = ClientFor(A, "AdminCompany");
        client.DefaultRequestHeaders.Add("X-Tenant-Id", B.ToString());

        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertNuncaTodasYSolo(A);
    }

    // ── AC6 — cabeza de red por las rutas propias ──────────────────────────────────────────────

    [Fact]
    public async Task Cabeza_de_red_por_las_rutas_propias_lee_solo_la_suya_no_la_red()
    {
        var response = await ClientFor(Cabeza, "AdminCompany").GetAsync(ByPersonUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Repo.Received(1).ListBiometricValidationsGroupedByPersonAsync(
            Arg.Is<TenantScope>(s => IsOnly(s, Cabeza)), Arg.Is(0), Arg.Any<int>(), Arg.Any<BiometricPersonGroupFilter?>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    // ── AC7 — rutas de un registro y de escritura siguen exigiendo compañía ─────────────────────

    [Theory]
    [InlineData("GET", "/api/v1/tramites/biometric-validations/by-person/detail?documentType=CC&documentNumber=1")]
    [InlineData("GET", "/api/v1/tramites/biometric-validations/c0000000-0000-4000-8000-000000000001")]
    [InlineData("GET", "/api/v1/tramites/biometric-validations/c0000000-0000-4000-8000-000000000001/audit")]
    [InlineData("POST", "/api/v1/tramites/identity-validation/stuck/requeue-all")]
    [InlineData("POST", "/api/v1/tramites/identity-validation/stuck/c0000000-0000-4000-8000-000000000001/requeue")]
    public async Task SuperAdmin_sin_header_en_una_ruta_de_registro_o_escritura_400(string method, string url)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), url);

        var response = await ClientFor(A, "SuperAdmin").SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("Falta header X-Tenant-Id");
    }

    // ── AC8 — registro de otra compañía por id ─────────────────────────────────────────────────

    [Fact]
    public async Task AdminCompany_pide_por_id_una_validacion_de_otra_compania_404()
    {
        var ajena = Guid.Parse("c0000000-0000-4000-8000-0000000000bb");
        _factory.Repo.GetBiometricByIdAsync(ajena, Arg.Any<CancellationToken>())
            .Returns(new ProcedureInstanceBiometricValidation { Id = ajena, TenantId = B, TokenHash = "h" });
        var client = ClientFor(A, "AdminCompany");
        client.DefaultRequestHeaders.Add("X-Tenant-Id", B.ToString());

        var response = await client.GetAsync($"{FlatUrl}/{ajena}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────

    private static bool IsOnly(TenantScope scope, Guid tenant) =>
        !scope.IsAll && scope.ReadTenantIds.Count == 1 && scope.ReadTenantIds.Contains(tenant);

    /// <summary>Ninguna llamada de lectura llevó «todas» ni otra compañía que <paramref name="tenant"/>.</summary>
    private void AssertNuncaTodasYSolo(Guid tenant)
    {
        var scopes = _factory.Repo.ReceivedCalls()
            .Concat(_factory.Outbox.ReceivedCalls())
            .SelectMany(c => c.GetArguments())
            .OfType<TenantScope>()
            .ToList();

        scopes.Should().NotBeEmpty("la ruta tuvo que consultar el repositorio");
        scopes.Should().OnlyContain(s => IsOnly(s, tenant));
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

    public sealed class IdentityFactory : WebApplicationFactory<Program>
    {
        public IProcedureInstanceRepository Repo { get; } = Substitute.For<IProcedureInstanceRepository>();

        public IIdentityValidationOutboxRepository Outbox { get; } = Substitute.For<IIdentityValidationOutboxRepository>();

        public IdentityFactory()
        {
            IReadOnlyList<BiometricPersonGroupProjection> persons =
            [
                Persona(A, "1020445118"),
                Persona(B, "1020445118"),
            ];
            Repo.ListBiometricValidationsGroupedByPersonAsync(
                    Arg.Any<TenantScope>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<BiometricPersonGroupFilter?>(),
                    Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns((persons, persons.Count));
            Repo.CountBiometricPersonsByEstadoAsync(
                    Arg.Any<TenantScope>(), Arg.Any<BiometricPersonGroupFilter?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(new Dictionary<string, int> { [BiometricEstados.Aprobado] = 2 });
            Repo.ListBiometricValidationsByTenantAsync(
                    Arg.Any<TenantScope>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<BiometricValidationListFilter?>(),
                    Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<ProcedureInstanceBiometricValidation>());
            Repo.CountBiometricValidationsByEstadoAsync(
                    Arg.Any<TenantScope>(), Arg.Any<BiometricValidationListFilter?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(new Dictionary<string, int>());
            Repo.ListBiometricValidationsForPersonAlertScanAsync(
                    Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<(string, string)>>(), Arg.Any<int>(),
                    Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<ProcedureInstanceBiometricValidation>());
            Repo.GetTenantNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
                .Returns(new Dictionary<Guid, string> { [A] = "Compañía A", [B] = "Compañía B" });
            Outbox.ListStuckAsync(Arg.Any<TenantScope>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<StuckIdentityValidationRow>());
            Outbox.ListStuckAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<StuckIdentityValidationRow>());
        }

        private static BiometricPersonGroupProjection Persona(Guid tenant, string documento) => new()
        {
            TenantId = tenant,
            LatestValidationId = Guid.NewGuid(),
            DocumentType = "CC",
            DocumentNumber = documento,
            DocumentTypeNorm = "CC",
            DocumentNumberNorm = documento,
            Name = "Persona",
            Status = BiometricEstados.Aprobado,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            Email = "p@correo.co",
            Provider = BiometricProviders.Mock,
            ValidationCount = 1,
        };

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => Repo);
                services.AddScoped(_ => Outbox);
                services.AddScoped<ITenantScopeResolver>(_ => new FakeScopeResolver());
            });
        }
    }

    /// <summary>La cabeza tiene una hija: el middleware le calcula <c>Group</c>; el resto, <c>Single</c>.</summary>
    private sealed class FakeScopeResolver : ITenantScopeResolver
    {
        public Task<TenantScope> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(tenantId == Cabeza
                ? TenantScope.Group(Cabeza, [Hija], GroupKind.MarcaBlanca)
                : TenantScope.Single(tenantId));
    }
}
