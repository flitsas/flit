using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.Tramites;

/// <summary>
/// HU #13289 y #13290 (Feature #13281 B, Épica #13202) — <c>/api/v1/public/manual-capture/{token}</c> con el pipeline HTTP real
/// y PostgreSQL real (la base local migrada). Todo ANÓNIMO y sin <c>X-Tenant-Id</c>: GET 200/404/410/409, consent 204/400 con
/// IP del servidor (nunca la del cuerpo) y constancia sobrescrita.
/// <para>Uso: <c>var (id, token) = await SeedAsync(); var r = await _client.GetAsync($"/api/v1/public/manual-capture/{token}")</c>.</para>
/// </summary>
public sealed class ManualCaptureEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly List<Guid> _validaciones = [];
    private readonly List<Guid> _personas = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ManualCaptureEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        using var db = NewDb();
        db.Tenants.Add(new Tenant
        {
            Id = _tenant, Code = $"H13289-{Guid.NewGuid():N}"[..20], LegalName = "Compania HU13289", TaxId = TestNit.Unique(),
            TenantType = "RENTING", IsGroupParent = false, IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
    }

    private static string Url(string token, string suffix = "") => $"/api/v1/public/manual-capture/{token}{suffix}";

    // ── GET: AC1, AC2, AC3 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_anonimo_sin_header_de_tenant_devuelve_la_vista_del_contrato_y_nada_mas()
    {
        var (_, token) = await SeedAsync();

        var response = await _client.GetAsync(Url(token), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var root = json.RootElement;
        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            ["fullName", "documentType", "documentNumber", "productName", "expiresAt", "consentTextVersion"]);
        root.GetProperty("fullName").GetString().Should().Be("Persona HU13289");
        root.GetProperty("documentType").GetString().Should().Be("CC");
        root.GetProperty("consentTextVersion").GetString().Should().Be(ManualCaptureConsent.TextVersion);
        root.GetProperty("productName").ValueKind.Should().Be(JsonValueKind.Null, "una prevalidación no tiene producto");
        root.GetProperty("expiresAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(20), TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task Get_token_inexistente_o_vacio_responde_404_con_codigo()
    {
        var response = await _client.GetAsync(Url(BiometricToken.Generate()), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        json.RootElement.GetProperty("code").GetString().Should().Be("not_found");
        json.RootElement.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Get_enlace_vencido_responde_410_expirada()
    {
        var (_, token) = await SeedAsync(expiresAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        var response = await _client.GetAsync(Url(token), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("expirada");
    }

    [Theory]
    [InlineData(BiometricEstados.PendienteRevisionManual)]
    [InlineData(BiometricEstados.Aprobado)]
    [InlineData(BiometricEstados.Rechazado)]
    public async Task Get_enlace_ya_usado_responde_409_estado_invalido(string estado)
    {
        var (_, token) = await SeedAsync(status: estado);

        var response = await _client.GetAsync(Url(token), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("estado_invalido");
    }

    [Fact]
    public async Task Get_token_de_un_enlace_regenerado_ya_no_existe()
    {
        var (id, viejo) = await SeedAsync();
        var nuevo = BiometricToken.Generate();
        await using (var db = NewDb())
        {
            await db.ProcedureInstanceBiometricValidations.Where(v => v.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.TokenHash, BiometricToken.Hash(nuevo)), Ct);
        }

        (await _client.GetAsync(Url(viejo), Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.GetAsync(Url(nuevo), Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_token_de_otro_proveedor_responde_404()
    {
        var (_, token) = await SeedAsync(provider: BiometricProviders.Mock, status: BiometricEstados.Enviado);

        (await _client.GetAsync(Url(token), Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── POST consent: AC4 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Consent_204_guarda_la_IP_del_servidor_y_no_la_del_cuerpo_y_audita_sin_IP()
    {
        var (id, token) = await SeedAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, Url(token, "/consent"))
        {
            Content = JsonContent.Create(new
            {
                accepted = true,
                textVersion = ManualCaptureConsent.TextVersion,
                ip = "10.9.9.9",
                consentAt = "2001-01-01T00:00:00Z",
            }),
        };
        request.Headers.Add("X-Forwarded-For", "203.0.113.7, 10.0.0.1");

        var response = await _client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(Ct));
        await using var db = NewDb();
        var v = await db.ProcedureInstanceBiometricValidations.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
        v.ConsentIp.Should().Be("203.0.113.7");
        v.ConsentAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        v.ConsentTextVersion.Should().Be(ManualCaptureConsent.TextVersion);
        v.Status.Should().Be(BiometricEstados.ManualActivo);

        var audit = await db.IdentityValidationAudits.AsNoTracking().Where(a => a.ValidationId == id).ToListAsync(Ct);
        var evento = audit.Should().ContainSingle().Which;
        evento.Stage.Should().Be(IdentityValidationAuditStages.ManualConsentimiento);
        $"{evento.Message}{evento.Detail}".Should().NotContain("203.0.113.7").And.NotContain("10.9.9.9");
    }

    [Fact]
    public async Task Consent_sin_IP_valida_no_inventa_una_y_sobrescribe_la_constancia_del_ciclo_previo()
    {
        // El TestServer no trae IP de conexión: sin cabecera X-Forwarded-For válida la columna queda nula (en un servidor
        // real cae a la IP de la conexión). Lo que importa aquí: nada de texto libre y la IP vieja no sobrevive.
        var (id, token) = await SeedAsync();
        await using (var db = NewDb())
        {
            await db.ProcedureInstanceBiometricValidations.Where(v => v.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(v => v.ConsentAt, DateTimeOffset.UtcNow.AddDays(-9))
                .SetProperty(v => v.ConsentIp, "198.51.100.1")
                .SetProperty(v => v.ConsentTextVersion, "version-vieja"), Ct);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, Url(token, "/consent"))
        {
            Content = JsonContent.Create(new { accepted = true, textVersion = ManualCaptureConsent.TextVersion }),
        };
        request.Headers.Add("X-Forwarded-For", "<script>no-es-una-ip</script>");
        var response = await _client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await using var check = NewDb();
        var v = await check.ProcedureInstanceBiometricValidations.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
        v.ConsentIp.Should().BeNull();
        v.ConsentAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        v.ConsentTextVersion.Should().Be(ManualCaptureConsent.TextVersion);
    }

    [Fact]
    public async Task Consent_con_accepted_false_responde_400_y_no_guarda()
    {
        var (id, token) = await SeedAsync();

        var response = await _client.PostAsJsonAsync(
            Url(token, "/consent"), new { accepted = false, textVersion = ManualCaptureConsent.TextVersion }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("consentimiento_no_aceptado");
        await using var db = NewDb();
        (await db.ProcedureInstanceBiometricValidations.AsNoTracking().SingleAsync(x => x.Id == id, Ct)).ConsentAt.Should().BeNull();
        (await db.IdentityValidationAudits.AsNoTracking().AnyAsync(a => a.ValidationId == id, Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Consent_con_version_incorrecta_responde_400()
    {
        var (_, token) = await SeedAsync();

        var response = await _client.PostAsJsonAsync(Url(token, "/consent"), new { accepted = true, textVersion = "x" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("version_texto_invalida");
    }

    [Fact]
    public async Task Consent_404_410_409_segun_el_estado_del_enlace()
    {
        (await Consent(BiometricToken.Generate())).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var (_, vencido) = await SeedAsync(expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        (await Consent(vencido)).StatusCode.Should().Be(HttpStatusCode.Gone);

        var (_, usado) = await SeedAsync(status: BiometricEstados.PendienteRevisionManual);
        (await Consent(usado)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── Infraestructura ─────────────────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> Consent(string token) =>
        _client.PostAsJsonAsync(Url(token, "/consent"), new { accepted = true, textVersion = ManualCaptureConsent.TextVersion }, Ct);

    /// <summary>Siembra una validación manual standalone (por persona) y devuelve su id y el token en claro.</summary>
    private async Task<(Guid Id, string Token)> SeedAsync(
        string status = BiometricEstados.ManualActivo,
        string provider = BiometricProviders.Manual,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? consentAt = null)
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        var documento = $"9{Random.Shared.NextInt64(10_000_000, 99_999_999)}";
        var persona = new Person
        {
            Id = Guid.NewGuid(), TenantId = _tenant, DocumentType = "CC", DocumentNumber = documento,
            FullName = "Persona HU13289", Email = $"p{documento}@example.test", PersonType = PersonTypes.Natural, CreatedAt = now,
        };
        db.Persons.Add(persona);
        await db.SaveChangesAsync(Ct);
        _personas.Add(persona.Id);

        var token = BiometricToken.Generate();
        var v = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(), TenantId = _tenant, ProcedureInstanceId = null, PersonId = persona.Id,
            Name = persona.FullName, DocumentType = "CC", DocumentNumber = documento, Email = persona.Email,
            RegisteredEmail = persona.Email, Status = status, Provider = provider,
            TokenHash = BiometricToken.Hash(token), ExpiresAt = expiresAt ?? now.AddHours(20),
            ManualActivatedAt = provider == BiometricProviders.Manual ? now.AddHours(-4) : null,
            ManualActivatedBy = provider == BiometricProviders.Manual ? Guid.NewGuid() : null,
            ConsentAt = consentAt, CreatedAt = now.AddDays(-1),
        };
        db.ProcedureInstanceBiometricValidations.Add(v);
        await db.SaveChangesAsync(Ct);
        _validaciones.Add(v.Id);
        return (v.Id, token);
    }

    private FlitDbContext NewDb() => _factory.Services.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();

    public void Dispose()
    {
        using var db = NewDb();
        db.IdentityValidationAudits.Where(a => a.TenantId == _tenant).ExecuteDelete();
        db.ProcedureInstanceBiometricValidations.Where(v => _validaciones.Contains(v.Id)).ExecuteDelete();
        db.Persons.Where(p => _personas.Contains(p.Id)).ExecuteDelete();
        db.Tenants.Where(t => t.Id == _tenant).ExecuteDelete();
    }
}
