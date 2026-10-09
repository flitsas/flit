using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Api.Authorization;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// Bug #12912 (hallazgos del review del PR #442).
/// <list type="bullet">
///   <item><b>Ley 1581</b> — en la configuración de mandatos del LADO OT (<c>/admin/ot/offices/{id}/mandatos/company-rules</c>
///   y <c>/admin/transit-offices/{id}/mandate-signers/companies</c>) un usuario no SuperAdmin ve las compañías
///   con grant directo y las de la red que ya le entregaron trámites; SuperAdmin ve toda la red. Escribir
///   sobre una compañía que el organismo no puede ver devuelve 404.</item>
///   <item><b>IDOR</b> — el grupo <c>/admin/transit-offices/{transitOfficeId}/mandate-signers</c> exige que
///   el OT pedido sea el del perfil del usuario; SuperAdmin libre. El subgrupo <c>/{id}/identity</c> no
///   lleva la guarda: sus cuatro rutas responden 410 sin tocar datos y su contrato lo fija
///   <c>AdminIdentityDeprecatedEndpointsTests</c>.</item>
/// </list>
/// Semilla: organismo A (perfil del tenant OT del usuario) y organismo B ajeno; Concesión H con grant a A
/// y su hija K sin trámites.
/// <para>Uso de ejemplo: <c>GET /api/v1/admin/ot/offices/{A}/mandatos/company-rules</c> como ot_admin de A
/// devuelve H pero no K.</para>
/// </summary>
public sealed class AdminOtMandatesNetworkScopeTests
    : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly SymmetricSecurityKey DummyKey =
        new(Encoding.UTF8.GetBytes(new string('k', 64)));

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    private readonly Guid _officeA = Guid.NewGuid();
    private readonly Guid _officeB = Guid.NewGuid();
    private readonly Guid _otTenantA = Guid.NewGuid();
    private readonly Guid _otUserA = Guid.NewGuid();
    private readonly Guid _superAdminTenantId = Guid.NewGuid();
    private readonly Guid _superAdminUserId = Guid.NewGuid();
    private readonly Guid _head = Guid.NewGuid();
    private readonly Guid _child = Guid.NewGuid();

    public AdminOtMandatesNetworkScopeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        SeedAsync().GetAwaiter().GetResult();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── Ley 1581: company-rules del hub OT ──────────────────────────────────────────────────────

    [Fact]
    public async Task CompanyRules_como_ot_admin_no_lista_la_red_sin_tramites()
    {
        AuthenticateOtUser();

        var ids = await ReadCompanyIdsAsync(
            await _client.GetAsync($"/api/v1/admin/ot/offices/{_officeA}/mandatos/company-rules", Ct), "items");

        ids.Should().Contain(_head, "la cabeza tiene grant directo al organismo");
        ids.Should().NotContain(_child, "la hija entra solo por la red y no ha entregado trámites (Ley 1581)");
    }

    [Fact]
    public async Task CompanyRules_como_SuperAdmin_lista_toda_la_red()
    {
        AuthenticateSuperAdmin();

        var ids = await ReadCompanyIdsAsync(
            await _client.GetAsync($"/api/v1/admin/ot/offices/{_officeA}/mandatos/company-rules", Ct), "items");

        ids.Should().Contain([_head, _child]);
    }

    [Fact]
    public async Task CompanyRules_default_signer_de_compania_no_visible_es_404_para_ot_admin()
    {
        AuthenticateOtUser();

        var response = await _client.PatchAsJsonAsync(
            $"/api/v1/admin/ot/offices/{_officeA}/mandatos/company-rules/{_child}/default-signer",
            new { defaultMandateSignerId = (Guid?)null },
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CompanyRules_delete_de_compania_no_visible_es_404_para_ot_admin()
    {
        AuthenticateOtUser();

        var response = await _client.DeleteAsync(
            $"/api/v1/admin/ot/offices/{_officeA}/mandatos/company-rules/{_child}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Ley 1581: consola de mandatarios del OT ─────────────────────────────────────────────────

    [Fact]
    public async Task MandateSigners_companies_como_ot_admin_no_lista_la_red_sin_tramites()
    {
        AuthenticateOtUser();

        var ids = await ReadCompanyIdsAsync(
            await _client.GetAsync($"/api/v1/admin/transit-offices/{_officeA}/mandate-signers/companies", Ct),
            "data");

        ids.Should().Contain(_head);
        ids.Should().NotContain(_child);
    }

    [Fact]
    public async Task MandateSigners_companies_como_SuperAdmin_lista_toda_la_red()
    {
        AuthenticateSuperAdmin();

        var ids = await ReadCompanyIdsAsync(
            await _client.GetAsync($"/api/v1/admin/transit-offices/{_officeA}/mandate-signers/companies", Ct),
            "data");

        ids.Should().Contain([_head, _child]);
    }

    // ── IDOR: alcance de OT en mandate-signers ──────────────────────────────────────────────────

    public static TheoryData<string, string> RutasDeOtroOrganismo => new()
    {
        { "GET", "" },
        { "GET", "/companies" },
        { "POST", "" },
        { "PUT", "/11111111-1111-4111-8111-111111111111" },
        { "POST", "/11111111-1111-4111-8111-111111111111/inactivate" },
        { "POST", "/11111111-1111-4111-8111-111111111111/reactivate" },
        { "GET", "/11111111-1111-4111-8111-111111111111/signature-image" },
    };

    [Theory]
    [MemberData(nameof(RutasDeOtroOrganismo))]
    public async Task MandateSigners_de_otro_organismo_es_403_para_ot_admin(string method, string suffix)
    {
        AuthenticateOtUser();

        var response = await SendAsync(method, $"/api/v1/admin/transit-offices/{_officeB}/mandate-signers{suffix}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("code").GetString().Should().Be("TRANSIT_OFFICE_FORBIDDEN");
    }

    [Fact]
    public async Task MandateSigners_del_propio_organismo_es_200_para_ot_admin()
    {
        AuthenticateOtUser();

        var response = await _client.GetAsync($"/api/v1/admin/transit-offices/{_officeA}/mandate-signers", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MandateSigners_de_cualquier_organismo_es_200_para_SuperAdmin()
    {
        AuthenticateSuperAdmin();

        var response = await _client.GetAsync($"/api/v1/admin/transit-offices/{_officeB}/mandate-signers", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Infraestructura ─────────────────────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> SendAsync(string method, string url) => method switch
    {
        "GET" => _client.GetAsync(url, Ct),
        "POST" => _client.PostAsJsonAsync(url, new { }, Ct),
        "PUT" => _client.PutAsJsonAsync(url, new { }, Ct),
        _ => throw new ArgumentOutOfRangeException(nameof(method)),
    };

    private static async Task<IReadOnlyList<Guid>> ReadCompanyIdsAsync(HttpResponseMessage response, string listProperty)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return
        [
            .. body.GetProperty(listProperty).EnumerateArray()
                .Select(e => e.GetProperty("companyTenantId").GetGuid()),
        ];
    }

    private void AuthenticateOtUser(string role = "ot_admin") =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken(role, _otTenantA, _otUserA, AdminAuthorization.TransitOfficeEntityType));

    private void AuthenticateSuperAdmin() =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken("SuperAdmin", _superAdminTenantId, _superAdminUserId));

    private async Task SeedAsync()
    {
        await using var db = CreateDbContext();

        db.TransitOffices.AddRange(NewOffice(_officeA, "OT A bug 12912"), NewOffice(_officeB, "OT B bug 12912"));
        db.Tenants.AddRange(
            NewTenant(_otTenantA, "OT A tenant bug 12912", "RENTING", isGroupParent: false, parent: null),
            NewTenant(_superAdminTenantId, "Empresa del SuperAdmin", "RENTING", isGroupParent: false, parent: null),
            NewTenant(_head, "Concesion H bug 12912", "CONCESION", isGroupParent: true, parent: null));
        // HU #13123 — el alta desde el OT audita con el usuario autenticado (FK changed_by).
        db.Users.Add(new User
        {
            Id = _otUserA,
            Email = $"otadmin-{_otUserA:N}@flit.local",
            DisplayName = "ot_admin de prueba",
            Status = "active",
            HomeTenantId = _otTenantA,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.Users.Add(new User
        {
            Id = _superAdminUserId,
            Email = $"superadmin-{_superAdminUserId:N}@flit.local",
            DisplayName = "SuperAdmin de prueba",
            Status = "active",
            HomeTenantId = _superAdminTenantId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(Ct);

        // Los triggers de identity.tenants exigen que la cabeza exista antes que la hija.
        db.Tenants.Add(NewTenant(_child, "Hija K bug 12912", "CONCESIONARIO", isGroupParent: false, parent: _head));
        db.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(),
            TenantId = _otTenantA,
            TransitOfficeId = _officeA,
            OperationMode = "dashboard",
            QuipuxReadOnly = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
        {
            Id = Guid.NewGuid(),
            TenantId = _head,
            TransitOfficeId = _officeA,
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(Ct);
    }

    private static TransitOffice NewOffice(Guid id, string name) => new()
    {
        Id = id,
        Code = $"R{Guid.NewGuid():N}"[..10],
        Name = name,
        DepartmentCode = "99",
        CityCode = "99999",
        IsActive = true,
    };

    private static Tenant NewTenant(Guid id, string legalName, string type, bool isGroupParent, Guid? parent) => new()
    {
        Id = id,
        Code = $"B12912-{Guid.NewGuid():N}"[..20],
        LegalName = legalName,
        TaxId = TestNit.Unique(),
        TenantType = type,
        IsGroupParent = isGroupParent,
        ParentTenantId = parent,
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private FlitDbContext CreateDbContext() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();

    private static string MintToken(string role, Guid tenantId, Guid userId, string? entityType = null)
    {
        var claims = new List<Claim>
        {
            new("sub", userId.ToString()),
            new("role", role),
            new("tenant_id", tenantId.ToString()),
        };
        if (entityType is not null)
        {
            claims.Add(new Claim(AdminAuthorization.EntityTypeClaimType, entityType));
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(DummyKey, SecurityAlgorithms.HmacSha256),
        });
    }

    public void Dispose()
    {
        using var db = CreateDbContext();

        // HU #13123 — mandatarios y firmas creados por las pruebas de alta desde el OT.
        var signerIds = db.MandateSigners.Where(m => m.TransitOfficeId == _officeA || m.TransitOfficeId == _officeB)
            .Select(m => m.Id).ToList();
        db.MandateSignerRepresentedCompanies.Where(x => signerIds.Contains(x.MandateSignerId)).ExecuteDelete();
        db.MandateSignerAssociatedCompanies.Where(x => signerIds.Contains(x.MandateSignerId)).ExecuteDelete();
        db.MandateSignerCompanies.Where(x => signerIds.Contains(x.MandateSignerId)).ExecuteDelete();
        db.MandateSignerTransitOffices.Where(x => signerIds.Contains(x.MandateSignerId)).ExecuteDelete();
        db.MandateSigners.Where(m => signerIds.Contains(m.Id)).ExecuteDelete();
        db.SignatureVault.Where(v => v.TenantId == _head || v.TenantId == _child || v.TenantId == _otTenantA)
            .ExecuteDelete();
        db.TenantConfigAuditLogs.Where(l => l.ChangedBy == _otUserA || l.ChangedBy == _superAdminUserId).ExecuteDelete();

        db.CompanyOtMandateRules.RemoveRange(db.CompanyOtMandateRules.Where(r =>
            r.TransitOfficeId == _officeA || r.TransitOfficeId == _officeB));
        db.TenantTransitOfficeGrants.RemoveRange(db.TenantTransitOfficeGrants.Where(g =>
            g.TenantId == _head || g.TenantId == _child));
        db.TransitOfficeProfiles.RemoveRange(db.TransitOfficeProfiles.Where(p => p.TenantId == _otTenantA));
        db.Users.RemoveRange(db.Users.Where(u => u.Id == _superAdminUserId || u.Id == _otUserA));
        db.SaveChanges();

        db.Tenants.RemoveRange(db.Tenants.Where(t => t.Id == _child));
        db.SaveChanges();

        db.Tenants.RemoveRange(db.Tenants.Where(t =>
            t.Id == _otTenantA || t.Id == _superAdminTenantId || t.Id == _head));
        db.TransitOffices.RemoveRange(db.TransitOffices.Where(o => o.Id == _officeA || o.Id == _officeB));
        db.SaveChanges();
    }

    // Bug #12912 (2ª vuelta review PR #442) - IDOR por body: TransitOfficeIds con un organismo ajeno.

    /// <summary>Cuerpo inválido a propósito (sin nombre): si la guarda deja pasar, el handler responde 422 sin escribir.</summary>
    private static object CuerpoConOrganismos(params Guid[] offices) => new
    {
        fullName = string.Empty,
        documentNumber = "1020304050",
        companyTenantIds = Array.Empty<Guid>(),
        transitOfficeIds = offices,
        email = "ana@flit.test",
    };

    [Fact]
    public async Task MandateSigners_alta_con_organismo_ajeno_en_el_body_es_403_para_ot_admin()
    {
        AuthenticateOtUser();

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/admin/transit-offices/{_officeA}/mandate-signers", CuerpoConOrganismos(_officeA, _officeB), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("code").GetString().Should().Be("TRANSIT_OFFICE_FORBIDDEN");
    }

    [Fact]
    public async Task MandateSigners_edicion_con_organismo_ajeno_en_el_body_es_403_para_ot_admin()
    {
        AuthenticateOtUser();

        var response = await _client.PutAsJsonAsync(
            $"/api/v1/admin/transit-offices/{_officeA}/mandate-signers/{Guid.NewGuid()}",
            CuerpoConOrganismos(_officeB),
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MandateSigners_alta_solo_con_el_propio_organismo_pasa_la_guarda()
    {
        AuthenticateOtUser();

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/admin/transit-offices/{_officeA}/mandate-signers", CuerpoConOrganismos(_officeA), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "la guarda pasa y el handler valida el cuerpo");
    }

    [Fact]
    public async Task MandateSigners_alta_multi_organismo_sigue_permitida_para_SuperAdmin()
    {
        AuthenticateSuperAdmin();

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/admin/transit-offices/{_officeA}/mandate-signers", CuerpoConOrganismos(_officeA, _officeB), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "SuperAdmin no tiene la restricción de organismo");
    }

    // ── HU #13123 — alta de mandatario desde el OT con validaciones compartidas ──────────────────

    private const string Documento = "1020304050";

    private async Task<Guid> SeedFirmaAsync(Guid companyTenantId, string documento = Documento)
    {
        await using var db = CreateDbContext();
        var id = Guid.NewGuid();
        var hoy = DateOnly.FromDateTime(DateTimeOffset.UtcNow.AddHours(-5).Date);
        db.SignatureVault.Add(new SignatureVaultEntity
        {
            Id = id,
            TenantId = companyTenantId,
            DocumentType = "CC",
            DocumentNumber = documento,
            FullName = "Ana Restrepo",
            SignatureHash = "sha",
            StoragePath = "vault/f.png",
            StorageSha256 = "sha",
            Estado = "activa",
            VigenciaDesde = hoy.AddDays(-1),
            VigenciaHasta = hoy.AddYears(1),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(Ct);
        return id;
    }

    private Task<HttpResponseMessage> PostAltaAsync(Guid office, Guid company, Guid? firma, string documento = Documento) =>
        _client.PostAsJsonAsync(
            $"/api/v1/admin/transit-offices/{office}/mandate-signers",
            new
            {
                fullName = "Ana Restrepo",
                documentNumber = documento,
                companyTenantIds = new[] { company },
                documentType = "CC",
                email = "ana@flit.test",
                transitOfficeIds = new[] { office },
                signatureVaultId = firma,
                signatureMethod = "baul",
            },
            Ct);

    private Task<HttpResponseMessage> PostModeloAsync(Guid office, Guid company, object cuerpo) =>
        _client.PostAsJsonAsync($"/api/v1/admin/transit-offices/{office}/mandate-signers", cuerpo, Ct);

    [Fact]
    public async Task HU13129_AC2_natural_con_baul_y_rango_es_201_y_la_lista_trae_el_estado_calculado()
    {
        AuthenticateSuperAdmin(); // el OT no gestiona mandatarios de compañías: la regla de negocio se prueba con el Super Admin
        var hoy = DateOnly.FromDateTime(DateTimeOffset.UtcNow.AddHours(-5).Date);
        var firma = await SeedFirmaAsync(_head);

        var response = await PostModeloAsync(_officeA, _head, new
        {
            fullName = "Ana Restrepo",
            documentNumber = Documento,
            email = "ana@example.com",
            companyTenantIds = new[] { _head },
            transitOfficeIds = new[] { _officeA },
            signerModel = "natural",
            signatureMethod = "baul",
            signatureVaultId = firma,
            validityKind = "range",
            validFrom = hoy.AddDays(-2).ToString("yyyy-MM-dd"),
            validTo = hoy.AddDays(5).ToString("yyyy-MM-dd"),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement
            .GetProperty("signingMeans").GetString().Should().Be("baul");

        var lista = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/admin/transit-offices/{_officeA}/mandate-signers", Ct);
        var fila = lista.GetProperty("data").EnumerateArray().Single();
        fila.GetProperty("signerModel").GetString().Should().Be("natural");
        fila.GetProperty("signatureMethod").GetString().Should().Be("baul");
        fila.GetProperty("validityKind").GetString().Should().Be("range");
        fila.GetProperty("validFrom").GetString().Should().Be(hoy.AddDays(-2).ToString("yyyy-MM-dd"));
        fila.GetProperty("validTo").GetString().Should().Be(hoy.AddDays(5).ToString("yyyy-MM-dd"));
        fila.GetProperty("validityStatus").GetString().Should().Be("por_vencer");
    }

    [Fact]
    public async Task HU13129_AC5_juridica_con_forma_de_firma_es_422_con_campo_y_mensaje()
    {
        AuthenticateSuperAdmin(); // el OT no gestiona mandatarios de compañías: la regla de negocio se prueba con el Super Admin

        var response = await PostModeloAsync(_officeA, _head, new
        {
            fullName = "Operadora UT",
            documentNumber = "900123456",
            companyTenantIds = new[] { _head },
            transitOfficeIds = new[] { _officeA },
            signerModel = "juridica",
            signatureMethod = "baul",
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var error = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("errors").EnumerateArray()
            .Single(e => e.GetProperty("field").GetString() == "signatureMethod");
        error.GetProperty("message").GetString().Should().Contain("Persona natural");
    }

    [Fact]
    public async Task HU13129_AC3_formato_en_blanco_es_201_sin_documento_ni_forma_de_firma()
    {
        AuthenticateSuperAdmin(); // el OT no gestiona mandatarios de compañías: la regla de negocio se prueba con el Super Admin

        var response = await PostModeloAsync(_officeA, _head, new
        {
            companyTenantIds = new[] { _head },
            transitOfficeIds = new[] { _officeA },
            signerModel = "formato_blanco",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var lista = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/admin/transit-offices/{_officeA}/mandate-signers", Ct);
        var fila = lista.GetProperty("data").EnumerateArray().Single();
        fila.GetProperty("fullName").GetString().Should().Be("Formato en blanco");
        fila.GetProperty("signerModel").GetString().Should().Be("formato_blanco");
    }

    [Fact]
    public async Task HU13129_AC6_rango_invertido_es_422()
    {
        AuthenticateSuperAdmin(); // el OT no gestiona mandatarios de compañías: la regla de negocio se prueba con el Super Admin

        var response = await PostModeloAsync(_officeA, _head, new
        {
            fullName = "Ana Restrepo",
            documentNumber = Documento,
            companyTenantIds = new[] { _head },
            transitOfficeIds = new[] { _officeA },
            signatureMethod = "baul",
            validityKind = "range",
            validFrom = "2026-12-10",
            validTo = "2026-12-01",
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("validTo");
    }

    [Fact]
    public async Task HU13123_AC1_ot_admin_no_registra_mandatario_de_compania_403_y_el_SuperAdmin_si_201()
    {
        var firma = await SeedFirmaAsync(_head);

        AuthenticateOtUser();
        var denegado = await PostAltaAsync(_officeA, _head, firma);
        denegado.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denegado.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString()
            .Should().Be("mandatario_de_compania");

        AuthenticateSuperAdmin();
        var response = await PostAltaAsync(_officeA, _head, firma);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task HU13123_AC1_firma_de_otra_persona_es_422_en_signatureVaultId()
    {
        AuthenticateSuperAdmin(); // el OT no gestiona mandatarios de compañías: la regla de negocio se prueba con el Super Admin
        var firmaOtraPersona = await SeedFirmaAsync(_head, documento: "9999999999");

        var response = await PostAltaAsync(_officeA, _head, firmaOtraPersona);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("errors")[0].GetProperty("field").GetString().Should().Be("signatureVaultId");
    }

    [Fact]
    public async Task HU13123_AC2_alta_en_otro_organismo_es_403_para_ot_admin()
    {
        AuthenticateOtUser();

        var response = await PostAltaAsync(_officeB, _head, null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task HU13123_AC3_gestor_de_tramites_OT_recibe_403_en_las_escrituras()
    {
        AuthenticateOtUser("gestor_tramites_ot");
        var firma = await SeedFirmaAsync(_head);
        var otro = Guid.NewGuid();
        var baseUrl = $"/api/v1/admin/transit-offices/{_officeA}/mandate-signers";

        (await PostAltaAsync(_officeA, _head, firma)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _client.PutAsJsonAsync($"{baseUrl}/{otro}", new { }, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _client.PostAsJsonAsync($"{baseUrl}/{otro}/inactivate", new { }, Ct)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await _client.PostAsJsonAsync($"{baseUrl}/{otro}/reactivate", new { }, Ct)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task HU13123_AC4_compania_ya_con_mandatario_en_el_organismo_es_422_por_exclusividad()
    {
        AuthenticateSuperAdmin(); // el OT no gestiona mandatarios de compañías: la regla de negocio se prueba con el Super Admin
        var firma = await SeedFirmaAsync(_head);
        (await PostAltaAsync(_officeA, _head, firma)).StatusCode.Should().Be(HttpStatusCode.Created);

        var segundo = await SeedFirmaAsync(_head, documento: "1020304051");
        var response = await PostAltaAsync(_officeA, _head, segundo, documento: "1020304051");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync(Ct))
            .Should().Contain("Ya existe un mandatario para esta empresa en este organismo.");
    }

    [Fact]
    public async Task HU13123_AC5_con_baul_y_sin_firma_es_422_con_mensaje_de_falta_de_firma_del_baul()
    {
        AuthenticateSuperAdmin(); // el OT no gestiona mandatarios de compañías: la regla de negocio se prueba con el Super Admin

        var response = await PostAltaAsync(_officeA, _head, null);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("no tiene una firma vigente en el baúl");
    }

    [Fact]
    public async Task HU13123_ajuste_sin_signatureVaultId_resuelve_la_firma_del_baul_y_no_expone_el_vault()
    {
        AuthenticateSuperAdmin(); // el OT no gestiona mandatarios de compañías: la regla de negocio se prueba con el Super Admin
        var firma = await SeedFirmaAsync(_head);

        var response = await PostAltaAsync(_officeA, _head, null);

        var texto = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, texto);
        JsonDocument.Parse(texto).RootElement.GetProperty("signingMeans").GetString().Should().Be("baul");
        texto.Should().NotContain(firma.ToString()).And.NotContain("signatureVaultId")
            .And.NotContain("storagePath").And.NotContain("vault/f.png");
    }

    [Fact]
    public async Task HU13123_ajuste_sin_vault_con_firma_solo_en_otro_tenant_es_422()
    {
        AuthenticateSuperAdmin(); // el OT no gestiona mandatarios de compañías: la regla de negocio se prueba con el Super Admin
        await SeedFirmaAsync(_child);

        var response = await PostAltaAsync(_officeA, _head, null);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task HU13123_AC6_ninguna_ruta_del_grupo_expone_la_lista_del_baul()
    {
        var rutas = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText ?? string.Empty)
            .Where(t => t.Contains("/transit-offices/{transitOfficeId:guid}/mandate-signers", StringComparison.Ordinal))
            .ToList();

        rutas.Should().NotBeEmpty();
        rutas.Should().NotContain(t => t.Contains("vault", StringComparison.OrdinalIgnoreCase)
            || t.EndsWith("/signatures", StringComparison.OrdinalIgnoreCase));

        AuthenticateOtUser();
        var body = await (await _client.GetAsync($"/api/v1/admin/transit-offices/{_officeA}/mandate-signers", Ct))
            .Content.ReadAsStringAsync(Ct);
        body.Should().NotContain("signatureVaultOptions").And.NotContain("vaultSignatures");
    }

    [Fact]
    public async Task HU13123_AC7_compania_inexistente_es_422_sin_confirmar_su_existencia()
    {
        AuthenticateSuperAdmin(); // el OT no gestiona mandatarios de compañías: la regla de negocio se prueba con el Super Admin
        // HU #13182b (D3, P7 del PO): el OT ya puede registrar el mandatario de CUALQUIER compañía activa aunque no esté
        // habilitada en su organismo (antes _child, fuera de su visibilidad, daba 422). Un id inexistente se sigue
        // rechazando, y antes que la firma: si se validara la firma primero, el 422 delataría que la compañía existe.
        var response = await PostAltaAsync(_officeA, Guid.NewGuid(), Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var contenido = await response.Content.ReadAsStringAsync(Ct);
        contenido.Should().Contain("companyTenantIds").And.NotContain("signatureVaultId");
    }

    // ── Baúl de firmas del propio OT: su mandatario (sin compañías) firma con el baúl del organismo ─────────────

    [Fact]
    public async Task BaulDelOt_ElAdminOt_ListaLasFirmasDelBaulDeSuOrganismo()
    {
        AuthenticateOtUser();
        var firma = await SeedFirmaAsync(_otTenantA);

        var response = await _client.GetAsync(
            $"/api/v1/admin/transit-offices/{_officeA}/signature-vault?documentType=CC&documentNumber={Documento}&soloVigentes=true",
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var ids = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("data").EnumerateArray()
            .Select(e => e.GetProperty("id").GetGuid());
        ids.Should().ContainSingle().Which.Should().Be(firma);
    }

    [Fact]
    public async Task BaulDelOt_OtroOrganismo_Es403()
    {
        AuthenticateOtUser();

        var response = await _client.GetAsync($"/api/v1/admin/transit-offices/{_officeB}/signature-vault", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task BaulDelOt_ElOperadorOt_NoLoGestiona()
    {
        AuthenticateOtUser("ot_operator");

        var response = await _client.GetAsync($"/api/v1/admin/transit-offices/{_officeA}/signature-vault", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task BaulDelOt_AltaDelMandatarioDelOtSinCompanias_ConFirmaDelBaulDelOt_Es201()
    {
        AuthenticateOtUser();
        var firma = await SeedFirmaAsync(_otTenantA);

        var response = await PostModeloAsync(_officeA, _otTenantA, new
        {
            fullName = "Ana Restrepo",
            documentNumber = Documento,
            documentType = "CC",
            email = "ana@flit.test",
            companyTenantIds = Array.Empty<Guid>(),
            transitOfficeIds = new[] { _officeA },
            signerModel = "natural",
            signatureMethod = "baul",
            signatureVaultId = firma,
            validityKind = "fixed",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement
            .GetProperty("signingMeans").GetString().Should().Be("baul");
    }
}
