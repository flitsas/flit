using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Flit.Api.Authorization;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// HU #12859 (Feature #12848, Épica #12751) — policy "solo ordena": Prelación documental
/// (<c>document-precedence</c>) queda abierta a <c>ot_admin</c> Y SuperAdmin
/// (<see cref="AdminAuthorization.OtAdminOrSuperAdminPolicy"/>, SIN el bypass de
/// <c>entity_type=TRANSIT_OFFICE</c> de <see cref="AdminAuthorization.OtModulePolicy"/>); Etiquetas
/// (<c>document-tags</c>) quedan EXCLUSIVAS de SuperAdmin
/// (<see cref="AdminAuthorization.SuperAdminPolicy"/>) — ni siquiera <c>ot_admin</c> las conserva.
/// </summary>
public sealed class AdminOtFeatureCAuthorizationTests
    : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly SymmetricSecurityKey DummyKey =
        new(Encoding.UTF8.GetBytes(new string('k', 64)));

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    private readonly Guid _officeId = Guid.NewGuid();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otUserId = Guid.NewGuid();

    private readonly Guid _superAdminTenantId = Guid.NewGuid();
    private readonly Guid _superAdminUserId = Guid.NewGuid();

    private readonly Guid _procedureTypeId = Guid.NewGuid();
    private readonly Guid _documentTypeId = Guid.NewGuid();

    public AdminOtFeatureCAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        SeedAsync().GetAwaiter().GetResult();
    }

    public static TheoryData<string> NonSuperAdminRoles => new() { "ot_admin", "gestor_tramites_ot" };

    public static TheoryData<string> RolesWithoutPrecedenceAccess => new() { "gestor_tramites_ot" };

    // ── AC1 — ot_admin conserva Prelación (200) ─────────────────────────────────────────────────

    [Fact]
    public async Task HU12859_AC1_GetDocumentPrecedence_AsOtAdmin_Returns200()
    {
        AuthenticateOtUser("ot_admin");

        var response = await _client.GetAsync(
            $"/api/v1/admin/ot/document-precedence?procedureTypeId={_procedureTypeId}",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HU12859_AC1_PatchDocumentPrecedence_AsOtAdmin_Returns200()
    {
        AuthenticateOtUser("ot_admin");

        var response = await _client.PatchAsJsonAsync(
            "/api/v1/admin/ot/document-precedence",
            new
            {
                procedure_type_id = _procedureTypeId,
                items = new[] { new { document_type_id = _documentTypeId, sort_order = 1 } },
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── AC1 — ot_admin pierde Etiquetas, prenda y overrides (403 en los tres) ──────────────────

    [Fact]
    public async Task HU12859_AC1_CreateDocumentTag_AsOtAdmin_Returns403()
    {
        AuthenticateOtUser("ot_admin");

        var response = await _client.PostAsJsonAsync(
            "/api/v1/admin/ot/document-tags",
            new { code = "URGENTE", name = "Urgente", color = "#FF0000" },
            TestContext.Current.CancellationToken);

        await AssertOtModuleForbiddenAsync(response);
    }

    [Fact]
    public async Task HU12859_AC1_ListDocumentTags_AsOtAdmin_Returns403()
    {
        AuthenticateOtUser("ot_admin");

        var response = await _client.GetAsync(
            "/api/v1/admin/ot/document-tags", TestContext.Current.CancellationToken);

        await AssertOtModuleForbiddenAsync(response);
    }

    [Fact]
    public async Task HU12859_AC1_DeleteDocumentTag_AsOtAdmin_Returns403()
    {
        AuthenticateOtUser("ot_admin");

        var response = await _client.DeleteAsync(
            $"/api/v1/admin/ot/document-tags/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        await AssertOtModuleForbiddenAsync(response);
    }

    // ── AC2 — cualquier otro rol OT (p. ej. gestor_tramites_ot) recibe 403 incluso en Prelación ─

    [Theory]
    [MemberData(nameof(RolesWithoutPrecedenceAccess))]
    public async Task HU12859_AC2_GetDocumentPrecedence_AsCustomOtRole_Returns403(string role)
    {
        AuthenticateOtUser(role);

        var response = await _client.GetAsync(
            $"/api/v1/admin/ot/document-precedence?procedureTypeId={_procedureTypeId}",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ForbiddenBody>(
            TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        body!.Error.Should().Be(
            AdminAuthorization.OtAdminOrSuperAdminForbiddenMessage,
            "la policy nueva es más estricta que OtModulePolicy: NO deja pasar por entity_type=TRANSIT_OFFICE");
    }

    [Theory]
    [MemberData(nameof(RolesWithoutPrecedenceAccess))]
    public async Task HU12859_AC2_PatchDocumentPrecedence_AsCustomOtRole_Returns403(string role)
    {
        AuthenticateOtUser(role);

        var response = await _client.PatchAsJsonAsync(
            "/api/v1/admin/ot/document-precedence",
            new
            {
                procedure_type_id = _procedureTypeId,
                items = new[] { new { document_type_id = _documentTypeId, sort_order = 1 } },
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(NonSuperAdminRoles))]
    public async Task HU12859_AC2_CreateDocumentTag_AsAnyNonSuperAdmin_Returns403(string role)
    {
        AuthenticateOtUser(role);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/admin/ot/document-tags",
            new { code = $"TAG-{role}", name = "Etiqueta", color = "#FF0000" },
            TestContext.Current.CancellationToken);

        await AssertOtModuleForbiddenAsync(response);
    }

    // ── AC3 — SuperAdmin con ?transitOfficeId válido sigue operando (200/201) en las 4 rutas ────

    [Fact]
    public async Task HU12859_AC3_GetDocumentPrecedence_AsSuperAdmin_Returns200()
    {
        AuthenticateSuperAdmin();

        var response = await _client.GetAsync(
            $"/api/v1/admin/ot/document-precedence?procedureTypeId={_procedureTypeId}&transitOfficeId={_officeId}",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HU12859_AC3_CreateDocumentTag_AsSuperAdmin_Returns201()
    {
        AuthenticateSuperAdmin();

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/admin/ot/document-tags?transitOfficeId={_officeId}",
            new { code = "HU12859", name = "HU 12859", color = "#123456" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>
    /// Asegura 403 con el mensaje de <see cref="AdminAuthorization.OtModuleForbiddenMessage"/>, NO
    /// <see cref="AdminAuthorization.ForbiddenMessage"/>: document-tags combina la
    /// <c>OtModulePolicy</c> del grupo con <c>SuperAdminPolicy</c> propia del endpoint — mismo
    /// patrón ya establecido por HU #12855 sobre Reglas/Requisitos
    /// (<see cref="AdminOtFeatureBAuthorizationTests"/>).
    /// </summary>
    private static async Task AssertOtModuleForbiddenAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var body = await response.Content.ReadFromJsonAsync<ForbiddenBody>(
            TestContext.Current.CancellationToken);

        body.Should().NotBeNull();
        body!.Error.Should().Be(AdminAuthorization.OtModuleForbiddenMessage);
    }

    private sealed record ForbiddenBody(string Error);

    private void AuthenticateOtUser(string role) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken(role, _tenantId, _otUserId, entityType: AdminAuthorization.TransitOfficeEntityType));

    private void AuthenticateSuperAdmin() =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken("SuperAdmin", _superAdminTenantId, _superAdminUserId));

    private async Task SeedAsync()
    {
        await using var db = CreateDbContext();

        db.TransitOffices.Add(NewOffice(_officeId, "OT authz Feature C"));
        db.Tenants.AddRange(
            NewTenant(_tenantId, "OT Feature C Authz"),
            NewTenant(_superAdminTenantId, "Empresa del SuperAdmin (sin perfil OT)"));

        db.Users.Add(new User
        {
            Id = _superAdminUserId,
            Email = $"superadmin-{_superAdminUserId:N}@flit.local",
            DisplayName = "SuperAdmin de prueba",
            Status = "active",
            HomeTenantId = _superAdminTenantId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.DocumentTypes.Add(new DocumentType
        {
            Id = _documentTypeId,
            Code = $"DOC{_documentTypeId:N}".ToUpperInvariant(),
            Name = "Documento de prueba HU12859",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        // FK real de ot_document_precedence.procedure_type_id -> tramites.procedure_types(id).
        db.ProcedureTypes.Add(new ProcedureType
        {
            Id = _procedureTypeId,
            Code = $"HU12859{_procedureTypeId:N}"[..30].ToUpperInvariant(),
            Name = "Trámite de prueba HU12859",
            Family = "MATRICULAS",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.TransitOfficeProfiles.Add(NewProfile(_tenantId, _officeId));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
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

    private static Tenant NewTenant(Guid id, string legalName) => new()
    {
        Id = id,
        Code = $"OT-AUTHZ-C-{Guid.NewGuid():N}"[..20],
        LegalName = legalName,
        TaxId = TestNit.Unique(),
        TenantType = "RENTING",
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static TransitOfficeProfile NewProfile(Guid tenantId, Guid officeId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        TransitOfficeId = officeId,
        OperationMode = "dashboard",
        QuipuxReadOnly = false,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private FlitDbContext CreateDbContext() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();

    private static string MintToken(string role, Guid tenantId, Guid userId, string? entityType = null)
    {
        var handler = new JsonWebTokenHandler();
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

        return handler.CreateToken(new SecurityTokenDescriptor
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

        // SaveChanges separado: EF no conoce la FK cruda ot_document_precedence.document_type_id
        // -> document_types(id) (sin navegación configurada), así que un solo batch puede intentar
        // borrar el catálogo antes que las filas hijas y violar la FK real de Postgres.
        db.OtDocumentPrecedences.RemoveRange(db.OtDocumentPrecedences.Where(p =>
            p.TenantId == _tenantId || p.TenantId == _superAdminTenantId));
        db.OtDocumentTags.RemoveRange(db.OtDocumentTags.Where(t =>
            t.TenantId == _tenantId || t.TenantId == _superAdminTenantId));
        db.SaveChanges();

        db.DocumentTypes.RemoveRange(db.DocumentTypes.Where(d => d.Id == _documentTypeId));
        db.ProcedureTypes.RemoveRange(db.ProcedureTypes.Where(p => p.Id == _procedureTypeId));
        db.TransitOfficeProfiles.RemoveRange(db.TransitOfficeProfiles.Where(p => p.TenantId == _tenantId));
        db.SaveChanges();

        db.Users.RemoveRange(db.Users.Where(u => u.Id == _superAdminUserId));
        db.Tenants.RemoveRange(db.Tenants.Where(t => t.Id == _tenantId || t.Id == _superAdminTenantId));
        db.TransitOffices.RemoveRange(db.TransitOffices.Where(o => o.Id == _officeId));
        db.SaveChanges();
    }
}
