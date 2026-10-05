using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.Tramites;

/// <summary>
/// HU #13287 (Feature #13280 A5, Épica #13202) — <c>POST /api/v1/tramites/biometric-validations/{id}/regenerate-manual-link</c>
/// con el pipeline HTTP real y PostgreSQL real (la base local migrada): solo Super Admin (200), AdminCompany 403, anónimo 401,
/// inexistente 404, fuera de <c>manual_activo</c> 409 <c>flujo_manual_no_activo</c>. El token nuevo reemplaza al anterior (solo
/// su hash queda en BD), el correo se pide una vez con el token en claro (notificador simulado: no sale correo real) y la
/// respuesta NO trae el token; un fallo de correo no revierte la regeneración y se informa.
/// <para>Uso: <c>await Post(validationId)</c> tras <c>Authenticate("SuperAdmin", ...)</c>; filas por persona en la compañía dueña.</para>
/// </summary>
public sealed class RegenerarEnlaceManualEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly SymmetricSecurityKey DummyKey = new(Encoding.UTF8.GetBytes(new string('k', 64)));

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly CapturingNotifier _notifier = new();

    private readonly Guid _dueno = Guid.NewGuid();
    private readonly Guid _tenantSuperAdmin = Guid.NewGuid();
    private readonly Guid _superAdmin = Guid.NewGuid();
    private readonly Guid _adminDueno = Guid.NewGuid();
    private readonly List<Guid> _validaciones = [];
    private readonly List<Guid> _personas = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public RegenerarEnlaceManualEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IManualCaptureLinkNotifier>();
            s.AddSingleton<IManualCaptureLinkNotifier>(_notifier);
        }));
        _client = _factory.CreateClient();
        using var db = NewDb();
        db.Tenants.AddRange(NewTenant(_dueno, "Compania duena HU13287"), NewTenant(_tenantSuperAdmin, "Super admin HU13287"));
        db.SaveChanges();
    }

    [Fact]
    public async Task SuperAdmin_regenera_el_enlace_el_token_viejo_deja_de_valer_y_el_correo_sale_una_vez()
    {
        var (id, hashViejo) = await SeedAsync();
        Authenticate("SuperAdmin", _tenantSuperAdmin, _superAdmin);

        var response = await Post(id);

        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("status").GetString().Should().Be("manual_activo");
        json.RootElement.GetProperty("provider").GetString().Should().Be("manual");
        json.RootElement.GetProperty("tenantId").GetGuid().Should().Be(_dueno);
        json.RootElement.GetProperty("emailEnviado").GetBoolean().Should().BeTrue();

        _notifier.Enviados.Should().ContainSingle();
        var enviado = _notifier.Enviados.Single();
        enviado.ValidationId.Should().Be(id);
        enviado.TenantId.Should().Be(_dueno);
        enviado.RecipientEmail.Should().Be("titular13287@example.test");
        body.Should().NotContain(enviado.Token, "el enlace en claro no viaja al navegador");
        body.ToLowerInvariant().Should().NotContain("tokenhash");

        await using var db = NewDb();
        var v = await db.ProcedureInstanceBiometricValidations.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
        v.TokenHash.Should().Be(BiometricToken.Hash(enviado.Token)).And.NotBe(hashViejo);
        (await db.ProcedureInstanceBiometricValidations.AsNoTracking().AnyAsync(x => x.TokenHash == hashViejo, Ct))
            .Should().BeFalse("el token anterior ya no se encuentra por hash: el público recibirá 404");
        v.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(24), TimeSpan.FromMinutes(2));
        v.Status.Should().Be(BiometricEstados.ManualActivo);
        v.ResendCount.Should().Be(1);

        var audit = await db.IdentityValidationAudits.AsNoTracking().Where(a => a.ValidationId == id).ToListAsync(Ct);
        audit.Select(a => a.Stage).Should().BeEquivalentTo([IdentityValidationAuditStages.ManualEnlaceRegenerado]);
        audit.Should().OnlyContain(a => a.TenantId == _dueno
            && !(a.Message ?? "").Contains(enviado.Token) && !(a.Detail ?? "").Contains(enviado.Token)
            && !(a.Message ?? "").Contains(v.TokenHash) && !(a.Detail ?? "").Contains(v.TokenHash));
    }

    [Fact]
    public async Task Si_el_correo_no_sale_la_regeneracion_se_conserva_y_se_informa()
    {
        var (id, hashViejo) = await SeedAsync();
        _notifier.Resultado = false;
        Authenticate("SuperAdmin", _tenantSuperAdmin, _superAdmin);

        var response = await Post(id);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        json.RootElement.GetProperty("emailEnviado").GetBoolean().Should().BeFalse();

        await using var db = NewDb();
        var v = await db.ProcedureInstanceBiometricValidations.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
        v.TokenHash.Should().NotBe(hashViejo);
        var stages = await db.IdentityValidationAudits.AsNoTracking().Where(a => a.ValidationId == id)
            .Select(a => a.Stage).ToListAsync(Ct);
        stages.Should().BeEquivalentTo(
            [IdentityValidationAuditStages.ManualEnlaceRegenerado, IdentityValidationAuditStages.ManualCorreoFallido]);
    }

    [Fact]
    public async Task AdminCompany_recibe_403_y_la_fila_no_cambia()
    {
        var (id, hashViejo) = await SeedAsync();
        Authenticate("AdminCompany", _dueno, _adminDueno);

        var response = await Post(id);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertHashAsync(id, hashViejo);
        _notifier.Enviados.Should().BeEmpty();
    }

    [Fact]
    public async Task Anonimo_recibe_401_y_la_fila_no_cambia()
    {
        var (id, hashViejo) = await SeedAsync();

        var response = await Post(id);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertHashAsync(id, hashViejo);
        _notifier.Enviados.Should().BeEmpty();
    }

    [Fact]
    public async Task Validacion_inexistente_responde_404()
    {
        Authenticate("SuperAdmin", _tenantSuperAdmin, _superAdmin);

        var response = await Post(Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(BiometricEstados.PendienteRevisionManual, BiometricProviders.Manual)]
    [InlineData(BiometricEstados.Rechazado, BiometricProviders.Manual)]
    [InlineData(BiometricEstados.EnProceso, BiometricProviders.Kyverum)]
    public async Task Fuera_de_manual_activo_responde_409_flujo_manual_no_activo(string status, string provider)
    {
        var (id, hashViejo) = await SeedAsync(status, provider);
        Authenticate("SuperAdmin", _tenantSuperAdmin, _superAdmin);

        var response = await Post(id);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("flujo_manual_no_activo");
        await AssertHashAsync(id, hashViejo);
        _notifier.Enviados.Should().BeEmpty();
        await using var db = NewDb();
        (await db.IdentityValidationAudits.AsNoTracking().AnyAsync(a => a.ValidationId == id, Ct)).Should().BeFalse();
    }

    // ── Infraestructura ─────────────────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> Post(Guid id)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/tramites/biometric-validations/{id}/regenerate-manual-link");
        return await _client.SendAsync(request, Ct);
    }

    private async Task AssertHashAsync(Guid id, string hash)
    {
        await using var db = NewDb();
        var v = await db.ProcedureInstanceBiometricValidations.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
        v.TokenHash.Should().Be(hash);
    }

    private async Task<(Guid Id, string Hash)> SeedAsync(
        string status = BiometricEstados.ManualActivo, string provider = BiometricProviders.Manual)
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        var documento = $"9{Random.Shared.NextInt64(10_000_000, 99_999_999)}";
        var persona = new Person
        {
            Id = Guid.NewGuid(), TenantId = _dueno, DocumentType = "CC", DocumentNumber = documento,
            FullName = "Persona de prueba", Email = "titular13287@example.test", PersonType = PersonTypes.Natural,
            CreatedAt = now,
        };
        db.Persons.Add(persona);
        await db.SaveChangesAsync(Ct);
        _personas.Add(persona.Id);

        var hash = BiometricToken.Hash(BiometricToken.Generate());
        var v = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(), TenantId = _dueno, ProcedureInstanceId = null, PersonId = persona.Id,
            Name = persona.FullName, DocumentType = "CC", DocumentNumber = documento, Email = persona.Email,
            RegisteredEmail = persona.Email, Status = status, Provider = provider,
            TokenHash = hash, ExpiresAt = now.AddHours(-2), CreatedAt = now.AddDays(-2),
            ManualActivatedBy = provider == BiometricProviders.Manual ? _superAdmin : null,
            ManualActivatedAt = provider == BiometricProviders.Manual ? now.AddDays(-1) : null,
        };
        db.ProcedureInstanceBiometricValidations.Add(v);
        await db.SaveChangesAsync(Ct);
        _validaciones.Add(v.Id);
        return (v.Id, hash);
    }

    private void Authenticate(string role, Guid tenant, Guid user) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintToken(role, tenant, user));

    private FlitDbContext NewDb() => _factory.Services.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();

    private static Tenant NewTenant(Guid id, string legalName) => new()
    {
        Id = id, Code = $"H13287-{Guid.NewGuid():N}"[..20], LegalName = legalName, TaxId = TestNit.Unique(),
        TenantType = "RENTING", IsGroupParent = false, IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
    };

    private static string MintToken(string role, Guid tenantId, Guid userId) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", userId.ToString()), new Claim("role", role), new Claim("tenant_id", tenantId.ToString()),
            ]),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(DummyKey, SecurityAlgorithms.HmacSha256),
        });

    /// <summary>Notificador simulado: registra los enlaces recibidos (token en claro) en lugar de enviar correo real.</summary>
    private sealed class CapturingNotifier : IManualCaptureLinkNotifier
    {
        public List<ManualCaptureLink> Enviados { get; } = [];
        public bool Resultado { get; set; } = true;

        public Task<bool> NotifyAsync(ManualCaptureLink link, CancellationToken ct = default)
        {
            Enviados.Add(link);
            return Task.FromResult(Resultado);
        }
    }

    public void Dispose()
    {
        using var db = NewDb();
        db.IdentityValidationAudits.Where(a => a.TenantId == _dueno).ExecuteDelete();
        db.ProcedureInstanceBiometricValidations.Where(v => _validaciones.Contains(v.Id)).ExecuteDelete();
        db.Persons.Where(p => _personas.Contains(p.Id)).ExecuteDelete();
        db.Tenants.Where(t => t.Id == _dueno || t.Id == _tenantSuperAdmin).ExecuteDelete();
    }
}
