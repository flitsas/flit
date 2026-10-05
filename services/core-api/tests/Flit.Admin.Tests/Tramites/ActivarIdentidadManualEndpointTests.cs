using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
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
/// HU #13284 (Feature #13280 A2, Épica #13202) — <c>POST /api/v1/tramites/biometric-validations/{id}/activate-manual</c> con el
/// pipeline HTTP real y PostgreSQL real (la base local migrada): solo Super Admin (200), AdminCompany 403, anónimo 401; el Super
/// Admin escribe sobre la compañía dueña de la fila aunque su token sea de otra y aunque mande el <c>X-Tenant-Id</c> equivocado;
/// aprobada y vigente responde 409 sin cambios; la respuesta no trae el token.
/// <para>Uso: <c>await Post(validationId)</c> tras <c>AuthenticateSuperAdmin()</c>; las filas se siembran por persona
/// (prevalidación standalone) en la compañía dueña.</para>
/// </summary>
public sealed class ActivarIdentidadManualEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly SymmetricSecurityKey DummyKey = new(Encoding.UTF8.GetBytes(new string('k', 64)));

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    private readonly Guid _dueno = Guid.NewGuid();
    private readonly Guid _otraCompania = Guid.NewGuid();
    private readonly Guid _tenantSuperAdmin = Guid.NewGuid();
    private readonly Guid _superAdmin = Guid.NewGuid();
    private readonly Guid _adminDueno = Guid.NewGuid();
    private readonly Guid _adminOtra = Guid.NewGuid();
    private readonly List<Guid> _validaciones = [];
    private readonly List<Guid> _personas = [];
    private readonly List<Guid> _firmantes = [];
    private readonly Guid _oficina = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ActivarIdentidadManualEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        using var db = NewDb();
        db.Tenants.AddRange(NewTenant(_dueno, "Compania duena HU13284"), NewTenant(_otraCompania, "Otra compania HU13284"),
            NewTenant(_tenantSuperAdmin, "Super admin HU13284"));
        db.SaveChanges();
    }

    // ── AC1 + cross-tenant ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SuperAdmin_de_otra_compania_activa_sobre_la_fila_del_dueno_aunque_mande_otro_X_Tenant_Id()
    {
        var id = await SeedAsync(BiometricEstados.EnProceso, kyverum: true);
        Authenticate("SuperAdmin", _tenantSuperAdmin, _superAdmin);

        var response = await Post(id, tenantHeader: _otraCompania);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var body = await response.Content.ReadAsStringAsync(Ct);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("status").GetString().Should().Be("manual_activo");
        json.RootElement.GetProperty("provider").GetString().Should().Be("manual");
        json.RootElement.GetProperty("tenantId").GetGuid().Should().Be(_dueno, "el tenant sale de la fila, no del token ni del header");
        json.RootElement.GetProperty("kyverumCancelado").GetBoolean().Should().BeTrue();
        body.ToLowerInvariant().Should().NotContain("token", "el enlace en claro no viaja al navegador");

        await using var db = NewDb();
        var v = await db.ProcedureInstanceBiometricValidations.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
        v.Status.Should().Be(BiometricEstados.ManualActivo);
        v.Provider.Should().Be(BiometricProviders.Manual);
        v.TenantId.Should().Be(_dueno);
        v.ManualActivatedBy.Should().Be(_superAdmin);
        v.ManualActivatedAt.Should().NotBeNull();
        v.TokenHash.Should().HaveLength(64).And.NotBe(new string('0', 64));
        v.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(24), TimeSpan.FromMinutes(2));
        v.KyverumVerificationId.Should().BeNull();
        v.WebhookSecretEncrypted.Should().BeNull();
        v.CaptureUrl.Should().BeNull();

        var audit = await db.IdentityValidationAudits.AsNoTracking().Where(a => a.ValidationId == id).ToListAsync(Ct);
        audit.Select(a => a.Stage).Should().BeEquivalentTo(
            [IdentityValidationAuditStages.ManualActivado, IdentityValidationAuditStages.KyverumCanceladoPorManual]);
        audit.Should().OnlyContain(a => a.TenantId == _dueno);
        audit.Single(a => a.Stage == IdentityValidationAuditStages.KyverumCanceladoPorManual)
            .KyverumVerificationId.Should().Be("kyv_ext_13284");
        audit.Should().OnlyContain(a => !(a.Message ?? "").Contains(v.TokenHash) && !(a.Detail ?? "").Contains(v.TokenHash));
    }

    [Fact]
    public async Task SuperAdmin_activa_una_prevalidacion_rechazada_sin_nada_de_Kyverum_en_vuelo()
    {
        var id = await SeedAsync(BiometricEstados.Rechazado, kyverum: true);
        Authenticate("SuperAdmin", _tenantSuperAdmin, _superAdmin);

        var response = await Post(id);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = NewDb();
        var stages = await db.IdentityValidationAudits.AsNoTracking().Where(a => a.ValidationId == id)
            .Select(a => a.Stage).ToListAsync(Ct);
        stages.Should().BeEquivalentTo([IdentityValidationAuditStages.ManualActivado]);
    }

    // ── AC3 — solo Super Admin ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AdminCompany_recibe_403_y_la_fila_no_cambia(bool deLaCompaniaDuena)
    {
        var id = await SeedAsync(BiometricEstados.EnProceso, kyverum: true);
        if (deLaCompaniaDuena)
            Authenticate("AdminCompany", _dueno, _adminDueno);
        else
            Authenticate("AdminCompany", _otraCompania, _adminOtra);

        var response = await Post(id);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertUntouchedAsync(id, BiometricEstados.EnProceso, BiometricProviders.Kyverum);
    }

    [Fact]
    public async Task Anonimo_recibe_401_y_la_fila_no_cambia()
    {
        var id = await SeedAsync(BiometricEstados.EnProceso, kyverum: true);

        var response = await Post(id);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertUntouchedAsync(id, BiometricEstados.EnProceso, BiometricProviders.Kyverum);
    }

    // ── AC2 — aprobada y vigente ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AprobadaYVigente_responde_409_con_codigo_claro_y_no_cambia()
    {
        var id = await SeedAsync(BiometricEstados.Aprobado, kyverum: true, vigente: true);
        Authenticate("SuperAdmin", _tenantSuperAdmin, _superAdmin);

        var response = await Post(id);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("identidad_aprobada_vigente");
        await AssertUntouchedAsync(id, BiometricEstados.Aprobado, BiometricProviders.Kyverum);
        await using var db = NewDb();
        (await db.IdentityValidationAudits.AsNoTracking().AnyAsync(a => a.ValidationId == id, Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task AprobadaVencida_si_se_activa()
    {
        var id = await SeedAsync(BiometricEstados.Aprobado, kyverum: true, vigente: false);
        Authenticate("SuperAdmin", _tenantSuperAdmin, _superAdmin);

        var response = await Post(id);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertUntouchedAsync(id, BiometricEstados.ManualActivo, BiometricProviders.Manual);
    }

    [Fact]
    public async Task Validacion_inexistente_responde_404()
    {
        Authenticate("SuperAdmin", _tenantSuperAdmin, _superAdmin);

        var response = await Post(Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── HU #13285 (A3) — mandatario y representante legal por el mismo endpoint ─────────────────

    [Fact]
    public async Task A3_Mandatario_no_aprobado_se_activa_conserva_MandateSignerId_y_no_crea_otra_validacion()
    {
        var (id, signer) = await SeedMandatarioAsync(BiometricEstados.EnProceso);
        Authenticate("SuperAdmin", _tenantSuperAdmin, _superAdmin);

        var response = await Post(id);

        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("status").GetString().Should().Be("manual_activo");
        json.RootElement.GetProperty("provider").GetString().Should().Be("manual");
        json.RootElement.GetProperty("origin").GetString().Should().Be("mandatario");

        await using var db = NewDb();
        var filas = await db.ProcedureInstanceBiometricValidations.AsNoTracking()
            .Where(x => x.MandateSignerId == signer).ToListAsync(Ct);
        filas.Should().ContainSingle("la activacion reutiliza la fila: una validacion propia por mandatario (HU #13246)");
        var v = filas.Single();
        v.Id.Should().Be(id);
        v.MandateSignerId.Should().Be(signer);
        v.PartyRole.Should().Be("mandatario");
        v.ProcedureInstanceId.Should().BeNull();
        v.PersonId.Should().BeNull();
        v.Status.Should().Be(BiometricEstados.ManualActivo);
        v.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(24), TimeSpan.FromMinutes(2));
        var audit = await db.IdentityValidationAudits.AsNoTracking().Where(a => a.ValidationId == id).ToListAsync(Ct);
        audit.Should().OnlyContain(a => a.PartyRole == "mandatario" && a.TenantId == _dueno);
    }

    [Fact]
    public async Task A3_Mandatario_enviado_activado_sigue_siendo_la_unica_validacion_de_la_ficha()
    {
        var (id, signer) = await SeedMandatarioAsync(BiometricEstados.Enviado);
        Authenticate("SuperAdmin", _tenantSuperAdmin, _superAdmin);

        (await Post(id)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = NewDb();
        (await db.ProcedureInstanceBiometricValidations.CountAsync(x => x.MandateSignerId == signer, Ct)).Should().Be(1);
    }

    [Fact]
    public async Task A3_Mandatario_aprobado_y_vigente_responde_409_y_no_cambia()
    {
        var (id, signer) = await SeedMandatarioAsync(BiometricEstados.Aprobado, vigente: true);
        Authenticate("SuperAdmin", _tenantSuperAdmin, _superAdmin);

        var response = await Post(id);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("identidad_aprobada_vigente");
        await using var db = NewDb();
        var v = await db.ProcedureInstanceBiometricValidations.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
        v.Status.Should().Be(BiometricEstados.Aprobado);
        v.MandateSignerId.Should().Be(signer);
        v.ManualActivatedBy.Should().BeNull();
    }

    [Fact]
    public async Task A3_Mandatario_AdminCompany_recibe_403()
    {
        var (id, _) = await SeedMandatarioAsync(BiometricEstados.EnProceso);
        Authenticate("AdminCompany", _dueno, _adminDueno);

        (await Post(id)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using var db = NewDb();
        (await db.ProcedureInstanceBiometricValidations.AsNoTracking().SingleAsync(x => x.Id == id, Ct))
            .Status.Should().Be(BiometricEstados.EnProceso);
    }

    [Fact]
    public async Task A3_RepresentanteLegal_persona_juridica_standalone_se_activa_con_origen_representante_legal()
    {
        var id = await SeedAsync(BiometricEstados.Rechazado, kyverum: true, juridica: true);
        Authenticate("SuperAdmin", _tenantSuperAdmin, _superAdmin);

        var response = await Post(id);

        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("status").GetString().Should().Be("manual_activo");
        json.RootElement.GetProperty("origin").GetString().Should().Be("representante_legal");
    }

    [Fact]
    public async Task A3_RepresentanteLegal_aprobado_y_vigente_responde_409()
    {
        var id = await SeedAsync(BiometricEstados.Aprobado, kyverum: false, vigente: true, juridica: true);
        Authenticate("SuperAdmin", _tenantSuperAdmin, _superAdmin);

        (await Post(id)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A3_Prevalidacion_natural_reporta_origen_prevalidacion()
    {
        var id = await SeedAsync(BiometricEstados.Rechazado, kyverum: false);
        Authenticate("SuperAdmin", _tenantSuperAdmin, _superAdmin);

        var response = await Post(id);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        json.RootElement.GetProperty("origin").GetString().Should().Be("prevalidacion");
    }

    // ── Infraestructura ─────────────────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> Post(Guid id, Guid? tenantHeader = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tramites/biometric-validations/{id}/activate-manual");
        if (tenantHeader is { } t)
            request.Headers.Add("X-Tenant-Id", t.ToString());
        return await _client.SendAsync(request, Ct);
    }

    private async Task AssertUntouchedAsync(Guid id, string status, string provider)
    {
        await using var db = NewDb();
        var v = await db.ProcedureInstanceBiometricValidations.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
        v.Status.Should().Be(status);
        v.Provider.Should().Be(provider);
        if (provider == BiometricProviders.Kyverum)
        {
            v.KyverumVerificationId.Should().Be("kyv_ext_13284");
            v.ManualActivatedBy.Should().BeNull();
        }
    }

    private async Task<Guid> SeedAsync(string status, bool kyverum, bool vigente = false, bool juridica = false)
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        var documento = $"9{Random.Shared.NextInt64(10_000_000, 99_999_999)}";
        var persona = new Person
        {
            Id = Guid.NewGuid(), TenantId = _dueno, DocumentType = "CC", DocumentNumber = documento,
            FullName = "Persona de prueba", Email = $"p{documento}@example.test", PersonType = juridica ? PersonTypes.Juridical : PersonTypes.Natural,
            CreatedAt = now,
        };
        db.Persons.Add(persona);
        await db.SaveChangesAsync(Ct);
        _personas.Add(persona.Id);

        var v = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(), TenantId = _dueno, ProcedureInstanceId = null, PersonId = persona.Id,
            Name = persona.FullName, DocumentType = "CC", DocumentNumber = documento, Email = persona.Email,
            RegisteredEmail = persona.Email, Status = status,
            Provider = kyverum ? BiometricProviders.Kyverum : BiometricProviders.Mock,
            TokenHash = new string('0', 64), ExpiresAt = now.AddHours(-1),
            KyverumVerificationId = kyverum ? "kyv_ext_13284" : null,
            CaptureUrl = kyverum ? "https://captura.example.test/x" : null,
            WebhookSecretEncrypted = kyverum ? "cifrado" : null,
            CreatedAt = now.AddDays(-1),
        };
        if (status == BiometricEstados.Aprobado)
        {
            v.ValidatedAt = vigente ? now.AddDays(-1) : now.AddDays(-60);
            v.ValidUntil = vigente ? now.AddDays(29) : now.AddDays(-30);
        }

        db.ProcedureInstanceBiometricValidations.Add(v);
        await db.SaveChangesAsync(Ct);
        _validaciones.Add(v.Id);
        return v.Id;
    }

    private async Task<(Guid ValidationId, Guid SignerId)> SeedMandatarioAsync(string status, bool vigente = false)
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        if (!await db.TransitOffices.AnyAsync(o => o.Id == _oficina, Ct))
        {
            db.TransitOffices.Add(new TransitOffice
            {
                Id = _oficina, Code = $"R{Guid.NewGuid():N}"[..10], Name = "OT HU13285", DepartmentCode = "99",
                CityCode = "99999", IsActive = true,
            });
            await db.SaveChangesAsync(Ct);
        }

        var documento = $"8{Random.Shared.NextInt64(10_000_000, 99_999_999)}";
        var signer = new MandateSigner
        {
            Id = Guid.NewGuid(), TransitOfficeId = _oficina, FullName = "Mandatario HU13285", DocumentType = "CC",
            DocumentNumber = documento, Email = $"m{documento}@example.test", IntegrityHash = new string('a', 64),
            RegisteredAt = now, IsActive = true, SignerModel = "natural", SignatureMethod = "biometria",
            ValidityKind = "fixed", CreatedAt = now,
        };
        db.MandateSigners.Add(signer);
        await db.SaveChangesAsync(Ct);
        _firmantes.Add(signer.Id);

        var v = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(), TenantId = _dueno, ProcedureInstanceId = null, PersonId = null,
            PartyRole = "mandatario", MandateSignerId = signer.Id,
            Name = signer.FullName, DocumentType = "CC", DocumentNumber = documento, Email = signer.Email!,
            RegisteredEmail = signer.Email!, Status = status, Provider = BiometricProviders.Kyverum,
            TokenHash = new string('0', 64), ExpiresAt = now.AddHours(-1), KyverumVerificationId = "kyv_ext_13285",
            CaptureUrl = "https://captura.example.test/m", WebhookSecretEncrypted = "cifrado", CreatedAt = now.AddDays(-1),
        };
        if (status == BiometricEstados.Aprobado)
        {
            v.ValidatedAt = vigente ? now.AddDays(-1) : now.AddDays(-60);
            v.ValidUntil = vigente ? now.AddDays(29) : now.AddDays(-30);
        }

        db.ProcedureInstanceBiometricValidations.Add(v);
        await db.SaveChangesAsync(Ct);
        _validaciones.Add(v.Id);
        return (v.Id, signer.Id);
    }

    private void Authenticate(string role, Guid tenant, Guid user) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintToken(role, tenant, user));

    private FlitDbContext NewDb() => _factory.Services.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();

    private static Tenant NewTenant(Guid id, string legalName) => new()
    {
        Id = id, Code = $"H13284-{Guid.NewGuid():N}"[..20], LegalName = legalName, TaxId = TestNit.Unique(),
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
        db.MandateSigners.Where(m => _firmantes.Contains(m.Id)).ExecuteDelete();
        db.TransitOffices.Where(o => o.Id == _oficina).ExecuteDelete();
        db.Tenants.Where(t => t.Id == _dueno || t.Id == _otraCompania || t.Id == _tenantSuperAdmin).ExecuteDelete();
    }
}
