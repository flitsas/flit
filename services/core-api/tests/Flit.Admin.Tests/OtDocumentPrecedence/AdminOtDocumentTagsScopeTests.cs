using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.OtDocumentPrecedence;

/// <summary>
/// HU #12858 (Feature #12848, Épica #12751) — Etiquetas documentales (POST/GET/DELETE
/// <c>/api/v1/admin/ot/document-tags</c>) resuelven el tenant objetivo con el MISMO helper
/// (<c>ResolveOtUserScopeAsync</c>) que Requisitos/Reglas/Prelación. Antes de esta HU el SuperAdmin
/// creaba/listaba/borraba etiquetas SIEMPRE en su propio tenant "fantasma" (el del JWT).
///
/// HU #12858_AC3 (ot_admin sigue usando su propio tenant sin exigir ?transitOfficeId) NO se prueba
/// aquí por HTTP: HU #12859 (mismo Feature #12848) restringe estos mismos endpoints a SuperAdmin
/// exclusivo, así que un ot_admin real recibe 403 por autorización antes de llegar al scoping (ver
/// <c>Flit.Admin.Tests.Authorization.AdminOtFeatureCAuthorizationTests</c>). La lógica genérica del
/// helper compartido ya está probada, por rol, en
/// <see cref="Flit.Admin.Tests.OtProfile.AdminOtUserScopeResolutionTests"/>.
/// </summary>
public sealed class AdminOtDocumentTagsScopeTests
    : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly SymmetricSecurityKey DummyKey =
        new(Encoding.UTF8.GetBytes(new string('k', 64)));

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    private readonly Guid _officeAId = Guid.NewGuid();
    private readonly Guid _tenantAId = Guid.NewGuid();

    private readonly Guid _superAdminTenantId = Guid.NewGuid();
    private readonly Guid _superAdminUserId = Guid.NewGuid();

    public AdminOtDocumentTagsScopeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        SeedAsync().GetAwaiter().GetResult();
    }

    [Fact] // HU12858_AC1 — POST ?transitOfficeId=X crea la etiqueta en el tenant OT dueño de X.
    public async Task HU12858_AC1_CreateDocumentTag_AsSuperAdmin_PersistsUnderRequestedOfficeTenant()
    {
        Authenticate("SuperAdmin", _superAdminTenantId, _superAdminUserId);
        var code = $"URGENTE-{Guid.NewGuid():N}"[..20].ToUpperInvariant();

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/admin/ot/document-tags?transitOfficeId={_officeAId}",
            new { code, name = "Urgente", color = "#FF0000" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = CreateDbContext();
        var entity = await db.OtDocumentTags.AsNoTracking()
            .SingleAsync(t => t.TenantId == _tenantAId && t.Code == code, TestContext.Current.CancellationToken);
        entity.TenantId.Should().Be(_tenantAId, "la etiqueta debe pertenecer al tenant OT dueño, no al del SuperAdmin");

        var superAdminHasTag = await db.OtDocumentTags.AsNoTracking()
            .AnyAsync(t => t.TenantId == _superAdminTenantId, TestContext.Current.CancellationToken);
        superAdminHasTag.Should().BeFalse("el tenant del SuperAdmin no debe recibir la etiqueta");
    }

    [Fact] // HU12858_AC1 — GET ?transitOfficeId=X lista las etiquetas del organismo pedido.
    public async Task HU12858_AC1_ListDocumentTags_AsSuperAdmin_ReturnsRequestedOfficeTags()
    {
        var tagId = await SeedTagAsync();
        Authenticate("SuperAdmin", _superAdminTenantId, _superAdminUserId);

        var response = await _client.GetAsync(
            $"/api/v1/admin/ot/document-tags?transitOfficeId={_officeAId}",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TagListDto>(
            TestContext.Current.CancellationToken);
        body!.Data.Should().ContainSingle(t => t.Id == tagId);
    }

    [Fact] // HU12858_AC1 — DELETE ?transitOfficeId=X borra la etiqueta del organismo pedido.
    public async Task HU12858_AC1_DeleteDocumentTag_AsSuperAdmin_DeletesFromRequestedOfficeTenant()
    {
        var tagId = await SeedTagAsync();
        Authenticate("SuperAdmin", _superAdminTenantId, _superAdminUserId);

        var response = await _client.DeleteAsync(
            $"/api/v1/admin/ot/document-tags/{tagId}?transitOfficeId={_officeAId}",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = CreateDbContext();
        var stillThere = await db.OtDocumentTags.AsNoTracking()
            .AnyAsync(t => t.Id == tagId, TestContext.Current.CancellationToken);
        stillThere.Should().BeFalse();
    }

    [Fact] // HU12858_AC2 — SuperAdmin sin ?transitOfficeId → 400, sin escribir en su propio tenant.
    public async Task HU12858_AC2_CreateDocumentTag_AsSuperAdmin_WithoutTransitOfficeId_Returns400()
    {
        Authenticate("SuperAdmin", _superAdminTenantId, _superAdminUserId);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/admin/ot/document-tags",
            new { code = "SIN-SCOPE", name = "Sin scope", color = "#00FF00" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var db = CreateDbContext();
        var superAdminHasTag = await db.OtDocumentTags.AsNoTracking()
            .AnyAsync(t => t.TenantId == _superAdminTenantId, TestContext.Current.CancellationToken);
        superAdminHasTag.Should().BeFalse("sin transitOfficeId, el SuperAdmin no debe escribir en ningún tenant");
    }

    [Fact]
    public async Task HU12858_AC2_ListDocumentTags_AsSuperAdmin_WithoutTransitOfficeId_Returns400()
    {
        Authenticate("SuperAdmin", _superAdminTenantId, _superAdminUserId);

        var response = await _client.GetAsync(
            "/api/v1/admin/ot/document-tags", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact] // HU12858_AC2 — DELETE sin ?transitOfficeId → 400, sin borrar nada en ningún tenant.
    public async Task HU12858_AC2_DeleteDocumentTag_AsSuperAdmin_WithoutTransitOfficeId_Returns400()
    {
        var tagId = await SeedTagAsync();
        Authenticate("SuperAdmin", _superAdminTenantId, _superAdminUserId);

        var response = await _client.DeleteAsync(
            $"/api/v1/admin/ot/document-tags/{tagId}",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var db = CreateDbContext();
        var stillThere = await db.OtDocumentTags.AsNoTracking()
            .AnyAsync(t => t.Id == tagId, TestContext.Current.CancellationToken);
        stillThere.Should().BeTrue("sin transitOfficeId, no debe borrarse la etiqueta de ningún tenant");
    }

    private void Authenticate(string role, Guid tenantId, Guid userId) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken(role, tenantId, userId));

    private async Task<Guid> SeedTagAsync()
    {
        var tagId = Guid.NewGuid();
        await using var db = CreateDbContext();
        db.OtDocumentTags.Add(new OtDocumentTagEntity
        {
            Id = tagId,
            TenantId = _tenantAId,
            Code = $"PRE{tagId:N}".ToUpperInvariant(),
            Name = "Etiqueta preexistente",
            Color = "#111111",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return tagId;
    }

    private async Task SeedAsync()
    {
        await using var db = CreateDbContext();

        db.TransitOffices.Add(NewOffice(_officeAId, "OT document-tags scope A"));
        db.Tenants.AddRange(
            NewTenant(_tenantAId, "OT Document Tags Scope A"),
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

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.TransitOfficeProfiles.Add(NewProfile(_tenantAId, _officeAId));
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
        Code = $"OT-DTG-{Guid.NewGuid():N}"[..20],
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

    private sealed record TagDto(Guid Id, string Code);

    private sealed record TagListDto(IReadOnlyList<TagDto> Data);

    public void Dispose()
    {
        using var db = CreateDbContext();

        db.OtDocumentTags.RemoveRange(db.OtDocumentTags.Where(t =>
            t.TenantId == _tenantAId || t.TenantId == _superAdminTenantId));
        db.TransitOfficeProfiles.RemoveRange(db.TransitOfficeProfiles.Where(p => p.TenantId == _tenantAId));
        db.SaveChanges();

        db.Users.RemoveRange(db.Users.Where(u => u.Id == _superAdminUserId));
        db.Tenants.RemoveRange(db.Tenants.Where(t => t.Id == _tenantAId || t.Id == _superAdminTenantId));
        db.TransitOffices.RemoveRange(db.TransitOffices.Where(o => o.Id == _officeAId));
        db.SaveChanges();
    }
}
