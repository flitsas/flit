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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.Tramites;

/// <summary>
/// HU #14305 (Feature #14304, Épica #14271) — <c>POST /api/v1/tramites/biometric-validations/{id}/manual-link/copy</c> con el
/// pipeline HTTP real, PostgreSQL real (la base local migrada) y el cifrado real de Data Protection: solo Super Admin (200,
/// <c>Cache-Control: no-store</c>), AdminCompany 403, anónimo 401, inexistente 404; vencido, captura enviada, enlace antiguo sin
/// cifrado y cifrado ilegible responden 409 con su <c>code</c>. Copiar NO cambia token ni hash y audita sin token ni URL.
/// <para>Uso: <c>await Post(validationId)</c> tras <c>Authenticate("SuperAdmin", ...)</c>.</para>
/// </summary>
public sealed class CopiarEnlaceManualEndpointTests : IClassFixture<CopiarEnlaceManualEndpointTests.Host>, IDisposable
{
    /// <summary>Un host para toda la clase, con la configuración por defecto (cifrado y correo reales de la app).</summary>
    public sealed class Host : HostPorClase
    {
        protected override WebApplicationFactory<Program> Configurar(WebApplicationFactory<Program> factory) => factory;
    }

    private static readonly SymmetricSecurityKey DummyKey = new(Encoding.UTF8.GetBytes(new string('k', 64)));

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    private readonly Guid _dueno = Guid.NewGuid();
    private readonly Guid _tenantSuperAdmin = Guid.NewGuid();
    private readonly Guid _superAdmin = Guid.NewGuid();
    private readonly Guid _adminDueno = Guid.NewGuid();
    private readonly List<Guid> _validaciones = [];
    private readonly List<Guid> _personas = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public CopiarEnlaceManualEndpointTests(Host host)
    {
        _factory = host.Factory;
        _client = _factory.CreateClient();
        using var db = NewDb();
        db.Tenants.AddRange(NewTenant(_dueno, "Compania duena HU14305"), NewTenant(_tenantSuperAdmin, "Super admin HU14305"));
        db.SaveChanges();
    }

    [Fact]
    public async Task SuperAdmin_recibe_el_enlace_completo_sin_cambiar_token_ni_hash_y_la_auditoria_no_lo_lleva()
    {
        var (id, token, hash, cifrado) = await SeedAsync();
        Authenticate("SuperAdmin", _tenantSuperAdmin, _superAdmin);

        var response = await Post(id);

        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        using var json = JsonDocument.Parse(body);
        var url = json.RootElement.GetProperty("url").GetString()!;
        url.Should().Contain("/verificacion/").And.EndWith(Uri.EscapeDataString(token));
        json.RootElement.GetProperty("expiresAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(5), TimeSpan.FromMinutes(2));
        body.ToLowerInvariant().Should().NotContain("tokenhash").And.NotContain("encrypted");

        await using var db = NewDb();
        var v = await db.ProcedureInstanceBiometricValidations.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
        v.TokenHash.Should().Be(hash);
        v.ManualCaptureTokenEncrypted.Should().Be(cifrado);
        v.ResendCount.Should().Be(0);

        var audit = await db.IdentityValidationAudits.AsNoTracking().Where(a => a.ValidationId == id).ToListAsync(Ct);
        audit.Select(a => a.Stage).Should().BeEquivalentTo([IdentityValidationAuditStages.ManualEnlaceCopiado]);
        audit.Should().OnlyContain(a => a.TenantId == _dueno
            && (a.Message ?? "").Contains(_superAdmin.ToString())
            && !(a.Message ?? "").Contains(token) && !(a.Detail ?? "").Contains(token)
            && !(a.Message ?? "").Contains("/verificacion/") && !(a.Detail ?? "").Contains("/verificacion/")
            && !(a.Message ?? "").Contains(hash) && !(a.Detail ?? "").Contains(hash));
    }

    [Fact]
    public async Task AdminCompany_recibe_403_y_no_se_audita()
    {
        var (id, _, _, _) = await SeedAsync();
        Authenticate("AdminCompany", _dueno, _adminDueno);

        var response = await Post(id);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertSinAuditoriaAsync(id);
    }

    [Fact]
    public async Task Anonimo_recibe_401()
    {
        var (id, _, _, _) = await SeedAsync();

        var response = await Post(id);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertSinAuditoriaAsync(id);
    }

    [Fact]
    public async Task Validacion_inexistente_responde_404()
    {
        Authenticate("SuperAdmin", _tenantSuperAdmin, _superAdmin);

        var response = await Post(Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Enlace_vencido_responde_409_enlace_vencido()
    {
        var (id, _, _, _) = await SeedAsync(expiresAt: DateTimeOffset.UtcNow.AddHours(-1));
        Authenticate("SuperAdmin", _tenantSuperAdmin, _superAdmin);

        var response = await Post(id);

        await AssertConflictAsync(response, "enlace_vencido");
        await AssertSinAuditoriaAsync(id);
    }

    [Fact]
    public async Task Captura_ya_enviada_responde_409_enlace_no_disponible()
    {
        var (id, _, _, _) = await SeedAsync(status: BiometricEstados.PendienteRevisionManual, protegido: false);
        Authenticate("SuperAdmin", _tenantSuperAdmin, _superAdmin);

        await AssertConflictAsync(await Post(id), "enlace_no_disponible");
    }

    [Fact]
    public async Task Enlace_antiguo_sin_token_cifrado_responde_409_enlace_no_disponible()
    {
        var (id, _, _, _) = await SeedAsync(protegido: false);
        Authenticate("SuperAdmin", _tenantSuperAdmin, _superAdmin);

        await AssertConflictAsync(await Post(id), "enlace_no_disponible");
    }

    [Fact]
    public async Task Cifrado_ilegible_por_llaves_rotadas_responde_409_enlace_no_disponible_y_no_500()
    {
        var (id, _, _, _) = await SeedAsync(cifradoCrudo: "CfDJ8-no-descifrable-con-las-llaves-actuales");
        Authenticate("SuperAdmin", _tenantSuperAdmin, _superAdmin);

        var response = await Post(id);

        await AssertConflictAsync(response, "enlace_no_disponible");
    }

    [Fact]
    public async Task Fuera_del_flujo_manual_responde_409_flujo_manual_no_activo()
    {
        var (id, _, _, _) = await SeedAsync(status: BiometricEstados.Aprobado);
        Authenticate("SuperAdmin", _tenantSuperAdmin, _superAdmin);

        await AssertConflictAsync(await Post(id), "flujo_manual_no_activo");
    }

    // ── Infraestructura ─────────────────────────────────────────────────────────────────────────

    private async Task AssertConflictAsync(HttpResponseMessage response, string code)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("code").GetString().Should().Be(code);
    }

    private async Task AssertSinAuditoriaAsync(Guid id)
    {
        await using var db = NewDb();
        (await db.IdentityValidationAudits.AsNoTracking().AnyAsync(a => a.ValidationId == id, Ct)).Should().BeFalse();
    }

    private async Task<HttpResponseMessage> Post(Guid id)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/tramites/biometric-validations/{id}/manual-link/copy");
        return await _client.SendAsync(request, Ct);
    }

    private async Task<(Guid Id, string Token, string Hash, string? Cifrado)> SeedAsync(
        string status = BiometricEstados.ManualActivo,
        DateTimeOffset? expiresAt = null,
        bool protegido = true,
        string? cifradoCrudo = null)
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        var documento = $"9{Random.Shared.NextInt64(10_000_000, 99_999_999)}";
        var persona = new Person
        {
            Id = Guid.NewGuid(), TenantId = _dueno, DocumentType = "CC", DocumentNumber = documento,
            FullName = "Persona de prueba", Email = "titular14305@example.test", PersonType = PersonTypes.Natural,
            CreatedAt = now,
        };
        db.Persons.Add(persona);
        await db.SaveChangesAsync(Ct);
        _personas.Add(persona.Id);

        var token = BiometricToken.Generate();
        var hash = BiometricToken.Hash(token);
        var protector = _factory.Services.GetRequiredService<IManualCaptureTokenProtector>();
        var cifrado = cifradoCrudo ?? (protegido ? protector.Protect(token) : null);
        var v = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(), TenantId = _dueno, ProcedureInstanceId = null, PersonId = persona.Id,
            Name = persona.FullName, DocumentType = "CC", DocumentNumber = documento, Email = persona.Email,
            RegisteredEmail = persona.Email, Status = status, Provider = BiometricProviders.Manual,
            TokenHash = hash, ManualCaptureTokenEncrypted = cifrado,
            ExpiresAt = expiresAt ?? now.AddHours(5), CreatedAt = now.AddDays(-2),
            ManualActivatedBy = _superAdmin, ManualActivatedAt = now.AddHours(-19),
        };
        db.ProcedureInstanceBiometricValidations.Add(v);
        await db.SaveChangesAsync(Ct);
        _validaciones.Add(v.Id);
        return (v.Id, token, hash, cifrado);
    }

    private void Authenticate(string role, Guid tenant, Guid user) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintToken(role, tenant, user));

    private FlitDbContext NewDb() => _factory.Services.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();

    private static Tenant NewTenant(Guid id, string legalName) => new()
    {
        Id = id, Code = $"H14305-{Guid.NewGuid():N}"[..20], LegalName = legalName, TaxId = TestNit.Unique(),
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

    public void Dispose()
    {
        using var db = NewDb();
        db.IdentityValidationAudits.Where(a => a.TenantId == _dueno).ExecuteDelete();
        db.ProcedureInstanceBiometricValidations.Where(v => _validaciones.Contains(v.Id)).ExecuteDelete();
        db.Persons.Where(p => _personas.Contains(p.Id)).ExecuteDelete();
        db.Tenants.Where(t => t.Id == _dueno || t.Id == _tenantSuperAdmin).ExecuteDelete();
    }
}
