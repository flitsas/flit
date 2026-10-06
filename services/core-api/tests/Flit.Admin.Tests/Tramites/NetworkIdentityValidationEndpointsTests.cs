using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Api.Authorization;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.Auditing;
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
/// HU #12708 — Validación de Identidad de la red (<c>/api/v1/tramites/network/identity-validations/**</c>)
/// con el pipeline HTTP real (middleware de tenant, <c>GroupHeadReadFilter</c>, auditoría de red y
/// filtro de permiso del módulo). Escenario: cabeza <c>P</c> con hijas <c>C1</c> y <c>C2</c>, compañía
/// ajena <c>X</c> y compañía sin red <c>S</c>. Los repositorios sustituidos devuelven SOLO las filas que el
/// alcance recibido permite, así la prueba ve el acotamiento de verdad y no un eco del mock.
/// </summary>
public sealed class NetworkIdentityValidationEndpointsTests
    : IClassFixture<NetworkIdentityValidationEndpointsTests.NetworkFactory>
{
    private const string ByPersonUrl = "/api/v1/tramites/network/identity-validations/by-person";
    private const string DetailUrl = "/api/v1/tramites/network/identity-validations/by-person/detail";

    private static readonly Guid P = Guid.Parse("f0000000-0000-4000-8000-000000000100");
    private static readonly Guid C1 = Guid.Parse("f0000000-0000-4000-8000-000000001100");
    private static readonly Guid C2 = Guid.Parse("f0000000-0000-4000-8000-000000001200");
    private static readonly Guid X = Guid.Parse("f0000000-0000-4000-8000-000000009900");
    private static readonly Guid S = Guid.Parse("f0000000-0000-4000-8000-000000005500");
    private static readonly Guid ValidacionC1 = Guid.Parse("f1000000-0000-4000-8000-0000000000c1");
    private static readonly Guid ValidacionX = Guid.Parse("f1000000-0000-4000-8000-000000000099");
    private static readonly Guid UserId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly string[] AdminCompanyPermissions = ["validaciones.read", "validaciones.manage", "dashboard.read"];

    private readonly NetworkFactory _factory;

    public NetworkIdentityValidationEndpointsTests(NetworkFactory factory)
    {
        _factory = factory;
        _factory.Repo.ClearReceivedCalls();
        _factory.Writer.ClearReceivedCalls();
    }

    // ── AC1 — listado consolidado ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cabeza_lee_las_personas_de_su_compania_y_de_sus_hijas_con_la_compania_de_cada_fila()
    {
        var response = await GetAsync(P, "AdminCompany", ByPersonUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonAsync(response);
        var persons = body.RootElement.GetProperty("persons").EnumerateArray().ToList();
        persons.Select(p => p.GetProperty("tenantId").GetGuid()).Should().BeEquivalentTo([P, C1, C2]);
        persons.Should().OnlyContain(p => p.GetProperty("tenantName").GetString()!.StartsWith("Compañía ", StringComparison.Ordinal));
        await _factory.Repo.Received(1).ListBiometricValidationsGroupedByPersonAsync(
            Arg.Is<TenantScope>(s => s.IsGroup && s.ReadTenantIds.SetEquals(new[] { P, C1, C2 })),
            Arg.Is(0), Arg.Any<int>(), Arg.Any<BiometricPersonGroupFilter?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    // ── AC4 — datos que ve la cabeza ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Cabeza_ve_enmascarado_el_correo_de_sus_hijas_y_nunca_su_enlace_de_captura()
    {
        var response = await GetAsync(P, "AdminCompany", ByPersonUrl);

        using var body = await JsonAsync(response);
        foreach (var person in body.RootElement.GetProperty("persons").EnumerateArray())
        {
            person.GetProperty("documentNumber").GetString().Should().NotBeNullOrEmpty();
            person.TryGetProperty("score", out _).Should().BeTrue();
            if (person.GetProperty("tenantId").GetGuid() == P)
            {
                // HU #12709 (AC3) — las filas de la propia cabeza viajan completas: son suyas.
                person.GetProperty("email").GetString().Should().Be("carolina.perez@correo.co");
                person.GetProperty("captureUrl").GetString().Should().NotBeNull();
                continue;
            }

            person.GetProperty("email").GetString().Should().MatchRegex(@"^.{1,2}\*\*\*@correo\.co$");
            person.GetProperty("captureUrl").ValueKind.Should().Be(JsonValueKind.Null);
            person.GetProperty("linkExpiresAt").ValueKind.Should().Be(JsonValueKind.Null);
        }
    }

    // ── AC2 — filtrar por una hija ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Filtrar_por_una_hija_devuelve_solo_sus_personas()
    {
        var response = await GetAsync(P, "AdminCompany", $"{ByPersonUrl}?childTenantId={C1}");

        using var body = await JsonAsync(response);
        body.RootElement.GetProperty("persons").EnumerateArray()
            .Select(p => p.GetProperty("tenantId").GetGuid()).Should().Equal(C1);
    }

    [Fact]
    public async Task Filtrar_por_una_compania_ajena_responde_403_sin_consultar()
    {
        var response = await GetAsync(P, "AdminCompany", $"{ByPersonUrl}?childTenantId={X}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorAsync(response)).Should().Be(NetworkScopePolicy.ChildOutOfScope);
        _factory.Repo.ReceivedCalls().Should().BeEmpty();
        // AC8 — el intento sobre la compañía ajena queda auditado.
        await _factory.Writer.Received(1).WriteAsync(
            Arg.Is<NetworkAccessAuditEntry>(e => e.Resource == NetworkAccessVocabulary.Resources.IdentityValidationsSearch
                && e.Result == NetworkAccessVocabulary.Results.Forbidden
                && e.ReachedTenantIds.SequenceEqual(new[] { X })),
            Arg.Any<CancellationToken>());
    }

    // ── AC3 — detalle y bitácora de una persona de una hija ────────────────────────────────────

    [Fact]
    public async Task Historial_de_una_persona_de_una_hija_en_solo_lectura_y_sin_enlace_de_captura()
    {
        var response = await GetAsync(P, "AdminCompany", $"{DetailUrl}?childTenantId={C1}&documentType=CC&documentNumber=1020445118");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonAsync(response);
        var validation = body.RootElement.GetProperty("validations")[0];
        validation.GetProperty("email").GetString().Should().Be("ca***@correo.co");
        validation.TryGetProperty("captureUrl", out var capture).Should().BeTrue();
        capture.ValueKind.Should().Be(JsonValueKind.Null, "la cabeza nunca recibe el enlace de captura");
        await _factory.Repo.Received(1).ListBiometricValidationsByPersonAsync(
            C1, "CC", "1020445118", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Historial_sin_compania_responde_400_porque_la_cedula_puede_estar_en_dos_hijas()
    {
        var response = await GetAsync(P, "AdminCompany", $"{DetailUrl}?documentType=CC&documentNumber=1020445118");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Historial_de_una_compania_ajena_responde_403_sin_consultar()
    {
        var response = await GetAsync(P, "AdminCompany", $"{DetailUrl}?childTenantId={X}&documentType=CC&documentNumber=1");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Repo.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Bitacora_de_una_validacion_de_una_hija_responde_200_y_se_audita()
    {
        var response = await GetAsync(P, "AdminCompany", $"/api/v1/tramites/network/identity-validations/{ValidacionC1}/audit");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Writer.Received(1).WriteAsync(
            Arg.Is<NetworkAccessAuditEntry>(e => e.Resource == NetworkAccessVocabulary.Resources.IdentityValidationsAudit
                && e.Result == NetworkAccessVocabulary.Results.Ok
                && e.ReachedTenantIds.SequenceEqual(new[] { C1 })),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("f1000000-0000-4000-8000-000000000099")] // validación de X, fuera de la red
    [InlineData("f1000000-0000-4000-8000-00000000dead")] // id inexistente
    public async Task Bitacora_fuera_de_la_red_responde_404_igual_que_un_id_inexistente(string validationId)
    {
        var response = await GetAsync(P, "AdminCompany", $"/api/v1/tramites/network/identity-validations/{validationId}/audit");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("Validación de identidad no encontrada.");
    }

    // ── AC5 — quién no tiene acceso ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Radicador_de_la_cabeza_recibe_403_network_role_required()
    {
        var response = await GetAsync(P, "Radicador", ByPersonUrl, ["dashboard.read", "tramites.read"]);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorAsync(response)).Should().Be(NetworkScopePolicy.RoleRequired);
        _factory.Repo.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("f0000000-0000-4000-8000-000000001100", "AdminCompany")] // hija C1 (AC6: tampoco ve a C2 ni a P)
    [InlineData("f0000000-0000-4000-8000-000000005500", "AdminCompany")] // compañía sin red
    [InlineData("f0000000-0000-4000-8000-000000005500", "SuperAdmin")]
    public async Task Hija_compania_sin_red_y_SuperAdmin_reciben_403_network_scope_required(string tenant, string role)
    {
        var response = await GetAsync(Guid.Parse(tenant), role, ByPersonUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorAsync(response)).Should().Be(NetworkScopePolicy.ScopeRequired);
        _factory.Repo.ReceivedCalls().Should().BeEmpty();
    }

    // ── AC9 — solo lectura ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("POST", "/api/v1/tramites/network/identity-validations")]
    [InlineData("PATCH", "/api/v1/tramites/network/identity-validations/f1000000-0000-4000-8000-0000000000c1")]
    [InlineData("POST", "/api/v1/tramites/network/identity-validations/f1000000-0000-4000-8000-0000000000c1/resend")]
    public async Task No_existen_rutas_de_escritura_en_la_red_de_identidad(string method, string url)
    {
        var client = ClientFor(P, "AdminCompany", AdminCompanyPermissions);
        using var request = new HttpRequestMessage(new HttpMethod(method), url);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> GetAsync(Guid tenant, string role, string url, string[]? permissions = null) =>
        ClientFor(tenant, role, permissions ?? AdminCompanyPermissions).GetAsync(url, TestContext.Current.CancellationToken);

    private static async Task<JsonDocument> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

    private static async Task<string?> ErrorAsync(HttpResponseMessage response)
    {
        using var body = await JsonAsync(response);
        return body.RootElement.GetProperty("error").GetString();
    }

    private HttpClient ClientFor(Guid tenantId, string role, IReadOnlyList<string> permissions)
    {
        var claims = new List<Claim>
        {
            new("sub", UserId.ToString()),
            new("role", role),
            new("role_code", role),
            new("tenant_id", tenantId.ToString()),
        };
        claims.AddRange(permissions.Select(p => new Claim("permissions", p)));
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 64))), SecurityAlgorithms.HmacSha256),
        });

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public sealed class NetworkFactory : WebApplicationFactory<Program>
    {
        public IProcedureInstanceRepository Repo { get; } = Substitute.For<IProcedureInstanceRepository>();

        public IIdentityValidationOutboxRepository Outbox { get; } = Substitute.For<IIdentityValidationOutboxRepository>();

        public INetworkAccessAuditWriter Writer { get; } = Substitute.For<INetworkAccessAuditWriter>();

        public NetworkFactory()
        {
            BiometricPersonGroupProjection[] all = [Persona(P), Persona(C1), Persona(C2), Persona(X)];
            Repo.ListBiometricValidationsGroupedByPersonAsync(
                    Arg.Any<TenantScope>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<BiometricPersonGroupFilter?>(),
                    Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var scope = call.ArgAt<TenantScope>(0);
                    IReadOnlyList<BiometricPersonGroupProjection> rows = all.Where(p => scope.CanRead(p.TenantId)).ToList();
                    return (rows, rows.Count);
                });
            Repo.CountBiometricPersonsByEstadoAsync(
                    Arg.Any<TenantScope>(), Arg.Any<BiometricPersonGroupFilter?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(new Dictionary<string, int> { [BiometricEstados.Aprobado] = 3 });
            Repo.ListBiometricValidationsForPersonAlertScanAsync(
                    Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<(string, string)>>(), Arg.Any<int>(),
                    Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<ProcedureInstanceBiometricValidation>());
            Repo.GetTenantNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
                .Returns(call => (IReadOnlyDictionary<Guid, string>)call.ArgAt<IReadOnlyCollection<Guid>>(0)
                    .ToDictionary(id => id, id => $"Compañía {id.ToString()[^4..]}"));
            Repo.ListBiometricValidationsByPersonAsync(
                    Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(call => (
                    (IReadOnlyList<ProcedureInstanceBiometricValidation>)[Validacion(Guid.NewGuid(), call.ArgAt<Guid>(0))],
                    1,
                    true));
            Repo.ListLinkedProceduresByIdentityDocumentsAsync(
                    Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<(string DocumentType, string DocumentNumber)>>(), Arg.Any<CancellationToken>())
                .Returns(new Dictionary<string, IReadOnlyList<LinkedProcedureSummary>>());
            Repo.GetBiometricByIdAsync(ValidacionC1, Arg.Any<CancellationToken>()).Returns(Validacion(ValidacionC1, C1));
            Repo.GetBiometricByIdAsync(ValidacionX, Arg.Any<CancellationToken>()).Returns(Validacion(ValidacionX, X));
            Repo.ListIdentityAuditByValidationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<IdentityValidationAuditEvent>());
            Outbox.ListStuckAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<StuckIdentityValidationRow>());
            Writer.WriteAsync(Arg.Any<NetworkAccessAuditEntry>(), Arg.Any<CancellationToken>()).Returns(true);
        }

        private static BiometricPersonGroupProjection Persona(Guid tenant) => new()
        {
            TenantId = tenant,
            LatestValidationId = Guid.NewGuid(),
            DocumentType = "CC",
            DocumentNumber = "1020445118",
            DocumentTypeNorm = "CC",
            DocumentNumberNorm = "1020445118",
            Name = "Carolina Pérez",
            Status = BiometricEstados.EnProceso,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            Email = "carolina.perez@correo.co",
            Provider = BiometricProviders.Kyverum,
            Score = 91,
            CaptureUrl = "https://captura.kyverum.test/abc",
            ValidationCount = 1,
        };

        private static ProcedureInstanceBiometricValidation Validacion(Guid id, Guid tenant) => new()
        {
            Id = id,
            TenantId = tenant,
            DocumentType = "CC",
            DocumentNumber = "1020445118",
            Name = "Carolina Pérez",
            Email = "carolina.perez@correo.co",
            Status = BiometricEstados.EnProceso,
            Provider = BiometricProviders.Kyverum,
            CaptureUrl = "https://captura.kyverum.test/abc",
            TokenHash = "h",
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
        };

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => Repo);
                services.AddScoped(_ => Outbox);
                services.AddSingleton(Writer);
                services.AddScoped<ITenantScopeResolver>(_ => new NetworkScopeResolver());
                services.AddScoped<ITransitOfficeTenantProbe>(_ => new NoTransitOffices());
            });
        }
    }

    private sealed class NetworkScopeResolver : ITenantScopeResolver
    {
        public Task<TenantScope> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(tenantId == P
                ? TenantScope.Group(P, [C1, C2], GroupKind.MarcaBlanca)
                : TenantScope.Single(tenantId));
    }

    private sealed class NoTransitOffices : ITransitOfficeTenantProbe
    {
        public Task<bool> IsTransitOfficeAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
