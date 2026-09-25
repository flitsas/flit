using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
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

namespace Flit.Admin.Tests.OtDocumentPrecedence;

/// <summary>
/// HU #12858 (Feature #12848, Épica #12751) — Prelación documental (GET/PATCH
/// <c>/api/v1/admin/ot/document-precedence</c>) resuelve el tenant objetivo con el MISMO helper
/// (<c>ResolveOtUserScopeAsync</c>) que ya usan Requisitos/Reglas. Antes de esta HU el SuperAdmin
/// leía/escribía SIEMPRE la prelación de su propio tenant "fantasma" (el del JWT), ignorando
/// <c>?transitOfficeId</c>.
///
/// Estos tests corren con <see cref="AdminAuthorization.OtAdminOrSuperAdminPolicy"/> ya aplicada
/// (HU #12859, mismo Feature): ot_admin SIGUE pudiendo llegar a document-precedence (a diferencia
/// de Reglas/Requisitos, que HU #12855 restringió a SuperAdmin exclusivo), así que el AC3 de esta
/// HU (ot_admin usa su propio tenant, ignorando ?transitOfficeId ajeno) se prueba aquí mismo por
/// HTTP end-to-end, sin necesidad de invocar el helper directo.
/// </summary>
public sealed class AdminOtDocumentPrecedenceScopeTests
    : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly SymmetricSecurityKey DummyKey =
        new(Encoding.UTF8.GetBytes(new string('k', 64)));

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    // Dos OT completos y distintos: el que se pide (A) y el vecino que NO se debe tocar (B).
    private readonly Guid _officeAId = Guid.NewGuid();
    private readonly Guid _tenantAId = Guid.NewGuid();
    private readonly Guid _officeBId = Guid.NewGuid();
    private readonly Guid _tenantBId = Guid.NewGuid();

    private readonly Guid _procedureTypeId = Guid.NewGuid();
    private readonly Guid _documentTypeId = Guid.NewGuid();

    // El SuperAdmin vive en un tenant propio SIN perfil de OT — como el de producción/QA.
    private readonly Guid _superAdminTenantId = Guid.NewGuid();
    private readonly Guid _superAdminUserId = Guid.NewGuid();

    public AdminOtDocumentPrecedenceScopeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        SeedAsync().GetAwaiter().GetResult();
    }

    [Fact] // HU12858_AC1 — GET ?transitOfficeId=X devuelve la prelación del organismo PEDIDO.
    public async Task HU12858_AC1_GetDocumentPrecedence_AsSuperAdmin_ReturnsRequestedOfficeConfiguration()
    {
        await SeedPrecedenceAsync(_tenantAId, sortOrder: 1);
        Authenticate("SuperAdmin", _superAdminTenantId, _superAdminUserId);

        var response = await _client.GetAsync(
            $"/api/v1/admin/ot/document-precedence?procedureTypeId={_procedureTypeId}&transitOfficeId={_officeAId}",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PrecedenceListDto>(
            TestContext.Current.CancellationToken);
        body!.Data.Should().Contain(d => d.DocumentTypeId == _documentTypeId && d.IsConfigured);
    }

    [Fact] // HU12858_AC1 — PATCH ?transitOfficeId=X escribe en el tenant DUEÑO de X, no en el vecino B.
    public async Task HU12858_AC1_PatchDocumentPrecedence_AsSuperAdmin_WritesRequestedOfficeTenant()
    {
        Authenticate("SuperAdmin", _superAdminTenantId, _superAdminUserId);

        var response = await _client.PatchAsJsonAsync(
            $"/api/v1/admin/ot/document-precedence?transitOfficeId={_officeAId}",
            new
            {
                procedure_type_id = _procedureTypeId,
                items = new[] { new { document_type_id = _documentTypeId, sort_order = 1 } },
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = CreateDbContext();
        var entity = await db.OtDocumentPrecedences.AsNoTracking()
            .SingleAsync(
                p => p.ProcedureTypeId == _procedureTypeId && p.DocumentTypeId == _documentTypeId,
                TestContext.Current.CancellationToken);
        entity.TenantId.Should().Be(_tenantAId, "la fila debe pertenecer al tenant OT dueño del organismo pedido");

        var superAdminHasRow = await db.OtDocumentPrecedences.AsNoTracking()
            .AnyAsync(p => p.TenantId == _superAdminTenantId, TestContext.Current.CancellationToken);
        superAdminHasRow.Should().BeFalse("el tenant del SuperAdmin no debe recibir la prelación");
    }

    [Fact] // HU12858_AC2 — SuperAdmin sin ?transitOfficeId → 400, sin tocar su propio tenant.
    public async Task HU12858_AC2_GetDocumentPrecedence_AsSuperAdmin_WithoutTransitOfficeId_Returns400()
    {
        Authenticate("SuperAdmin", _superAdminTenantId, _superAdminUserId);

        var response = await _client.GetAsync(
            $"/api/v1/admin/ot/document-precedence?procedureTypeId={_procedureTypeId}",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task HU12858_AC2_PatchDocumentPrecedence_AsSuperAdmin_WithoutTransitOfficeId_Returns400()
    {
        Authenticate("SuperAdmin", _superAdminTenantId, _superAdminUserId);

        var response = await _client.PatchAsJsonAsync(
            "/api/v1/admin/ot/document-precedence",
            new
            {
                procedure_type_id = _procedureTypeId,
                items = new[] { new { document_type_id = _documentTypeId, sort_order = 1 } },
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var db = CreateDbContext();
        var superAdminHasRow = await db.OtDocumentPrecedences.AsNoTracking()
            .AnyAsync(p => p.TenantId == _superAdminTenantId, TestContext.Current.CancellationToken);
        superAdminHasRow.Should().BeFalse("sin transitOfficeId, el SuperAdmin no debe escribir en ningún tenant");
    }

    [Fact] // HU12858_AC3 — ot_admin usa su propio tenant; ?transitOfficeId ajeno se IGNORA (IDOR).
    public async Task HU12858_AC3_GetDocumentPrecedence_AsOtAdmin_IgnoresForeignTransitOfficeId()
    {
        await SeedPrecedenceAsync(_tenantAId, sortOrder: 1);
        await SeedPrecedenceAsync(_tenantBId, sortOrder: 2);

        // ot_admin del tenant A intenta leer el organismo B inyectando el query param.
        Authenticate("ot_admin", _tenantAId, Guid.NewGuid());

        var response = await _client.GetAsync(
            $"/api/v1/admin/ot/document-precedence?procedureTypeId={_procedureTypeId}&transitOfficeId={_officeBId}",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PrecedenceListDto>(
            TestContext.Current.CancellationToken);
        body!.Data.Single(d => d.DocumentTypeId == _documentTypeId).SortOrder.Should().Be(1,
            "ot_admin debe ver SU propia prelación (sortOrder=1), no la del organismo B (sortOrder=2)");
    }

    private void Authenticate(string role, Guid tenantId, Guid userId) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken(role, tenantId, userId));

    private async Task SeedPrecedenceAsync(Guid tenantId, short sortOrder)
    {
        await using var db = CreateDbContext();
        db.OtDocumentPrecedences.Add(new OtDocumentPrecedenceEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureTypeId = _procedureTypeId,
            DocumentTypeId = _documentTypeId,
            SortOrder = sortOrder,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedAsync()
    {
        await using var db = CreateDbContext();

        db.TransitOffices.AddRange(
            NewOffice(_officeAId, "OT document-precedence scope A"),
            NewOffice(_officeBId, "OT document-precedence scope B"));

        db.Tenants.AddRange(
            NewTenant(_tenantAId, "OT Document Precedence Scope A"),
            NewTenant(_tenantBId, "OT Document Precedence Scope B"),
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
            Name = "Documento de prueba HU12858",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        // FK real de ot_document_precedence.procedure_type_id -> tramites.procedure_types(id):
        // sin esta fila, cualquier PATCH/seed de prelación para _procedureTypeId falla con 23503.
        db.ProcedureTypes.Add(new ProcedureType
        {
            Id = _procedureTypeId,
            Code = $"HU12858{_procedureTypeId:N}"[..30].ToUpperInvariant(),
            Name = "Trámite de prueba HU12858",
            Family = "MATRICULAS",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.TransitOfficeProfiles.AddRange(
            NewProfile(_tenantAId, _officeAId),
            NewProfile(_tenantBId, _officeBId));

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
        Code = $"OT-DPR-{Guid.NewGuid():N}"[..20],
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

    private static string MintToken(string role, Guid tenantId, Guid userId)
    {
        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", userId.ToString()),
                new Claim("role", role),
                new Claim("tenant_id", tenantId.ToString()),
            ]),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(DummyKey, SecurityAlgorithms.HmacSha256),
        });
    }

    private sealed record PrecedenceItemDto(
        [property: JsonPropertyName("document_type_id")] Guid DocumentTypeId,
        [property: JsonPropertyName("sort_order")] short SortOrder,
        [property: JsonPropertyName("is_configured")] bool IsConfigured);

    private sealed record PrecedenceListDto(IReadOnlyList<PrecedenceItemDto> Data);

    public void Dispose()
    {
        using var db = CreateDbContext();

        db.OtDocumentPrecedences.RemoveRange(db.OtDocumentPrecedences.Where(p =>
            p.TenantId == _tenantAId || p.TenantId == _tenantBId || p.TenantId == _superAdminTenantId));
        db.TransitOfficeProfiles.RemoveRange(db.TransitOfficeProfiles.Where(p =>
            p.TenantId == _tenantAId || p.TenantId == _tenantBId));
        db.SaveChanges();

        db.DocumentTypes.RemoveRange(db.DocumentTypes.Where(d => d.Id == _documentTypeId));
        db.ProcedureTypes.RemoveRange(db.ProcedureTypes.Where(p => p.Id == _procedureTypeId));
        db.Users.RemoveRange(db.Users.Where(u => u.Id == _superAdminUserId));
        db.Tenants.RemoveRange(db.Tenants.Where(t =>
            t.Id == _tenantAId || t.Id == _tenantBId || t.Id == _superAdminTenantId));
        db.TransitOffices.RemoveRange(db.TransitOffices.Where(o =>
            o.Id == _officeAId || o.Id == _officeBId));
        db.SaveChanges();
    }
}
