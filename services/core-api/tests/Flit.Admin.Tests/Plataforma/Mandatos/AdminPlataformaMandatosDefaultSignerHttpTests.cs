using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
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

namespace Flit.Admin.Tests.Plataforma.Mandatos;

/// <summary>
/// HU #13126 (Epic #13090, F1) — <c>PATCH /api/v1/admin/plataforma/mandatos/ot/{officeId}/default-signer</c>
/// responde <c>400 mandatario_default_invalido</c> (igual que la ruta del OT) cuando el mandatario por
/// defecto no es válido para el organismo; antes caía en un 400 sin cuerpo (<c>MapWrite</c> no mapeaba
/// <c>InvalidDefaultSigner</c>). El caso válido sigue respondiendo 200 y el resto de estados no cambia.
/// <para>Uso de ejemplo: como SuperAdmin, <c>PATCH .../ot/{office}/default-signer</c> con
/// <c>{ "defaultMandateSignerId": "&lt;guid inexistente&gt;" }</c> ⇒ <c>400 { "error": "mandatario_default_invalido" }</c>.</para>
/// </summary>
public sealed class AdminPlataformaMandatosDefaultSignerHttpTests
    : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly SymmetricSecurityKey DummyKey =
        new(Encoding.UTF8.GetBytes(new string('k', 64)));

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _office = Guid.NewGuid();
    private readonly Guid _superAdminTenantId = Guid.NewGuid();
    private readonly Guid _superAdminUserId = Guid.NewGuid();
    private readonly Guid _validSigner = Guid.NewGuid();

    public AdminPlataformaMandatosDefaultSignerHttpTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        SeedAsync().GetAwaiter().GetResult();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintSuperAdminToken());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Url => $"/api/v1/admin/plataforma/mandatos/ot/{_office}/default-signer";

    [Fact]
    public async Task AC1_DefaultInvalido_Responde400_MandatarioDefaultInvalido()
    {
        var response = await _client.PatchAsJsonAsync(Url, new { defaultMandateSignerId = Guid.NewGuid() }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("error").GetString().Should().Be("mandatario_default_invalido");
    }

    [Fact]
    public async Task AC2_DefaultValido_Responde200_ConLaConfiguracionActualizada()
    {
        var response = await _client.PatchAsJsonAsync(Url, new { defaultMandateSignerId = _validSigner }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("defaultMandateSignerId").GetGuid().Should().Be(_validSigner);
    }

    [Fact]
    public async Task AC3_OrganismoInexistente_SigueRespondiendo404()
    {
        var response = await _client.PatchAsJsonAsync(
            $"/api/v1/admin/plataforma/mandatos/ot/{Guid.NewGuid()}/default-signer",
            new { defaultMandateSignerId = (Guid?)null },
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task SeedAsync()
    {
        await using var db = CreateDbContext();
        db.TransitOffices.Add(new TransitOffice
        {
            Id = _office,
            Code = $"R{Guid.NewGuid():N}"[..10],
            Name = "OT HU13126",
            DepartmentCode = "99",
            CityCode = "99999",
            IsActive = true,
        });
        db.Tenants.Add(new Tenant
        {
            Id = _superAdminTenantId,
            Code = $"H13126-{Guid.NewGuid():N}"[..20],
            LegalName = "Empresa del SuperAdmin HU13126",
            TaxId = TestNit.Unique(),
            TenantType = "RENTING",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(Ct);

        db.Users.Add(new User
        {
            Id = _superAdminUserId,
            Email = $"superadmin-{_superAdminUserId:N}@flit.local",
            DisplayName = "SuperAdmin de prueba",
            Status = "active",
            HomeTenantId = _superAdminTenantId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        var now = DateTimeOffset.UtcNow;
        db.MandateSigners.Add(new MandateSigner
        {
            Id = _validSigner,
            TransitOfficeId = _office,
            FullName = "Ana Restrepo",
            DocumentType = "CC",
            DocumentNumber = "1020304099",
            IntegrityHash = new string('a', 64),
            RegisteredAt = now,
            IsActive = true,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(Ct);
    }

    private FlitDbContext CreateDbContext() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();

    private string MintSuperAdminToken() =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", _superAdminUserId.ToString()),
                new Claim("role", "SuperAdmin"),
                new Claim("tenant_id", _superAdminTenantId.ToString()),
            ]),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(DummyKey, SecurityAlgorithms.HmacSha256),
        });

    public void Dispose()
    {
        using var db = CreateDbContext();
        // ExecuteDelete: la config lleva token de concurrencia (row_version por trigger) y RemoveRange fallaría.
        db.TransitOfficeMandateConfigs.Where(c => c.TransitOfficeId == _office).ExecuteDelete();
        db.MandateSigners.Where(s => s.Id == _validSigner).ExecuteDelete();
        db.Users.Where(u => u.Id == _superAdminUserId).ExecuteDelete();
        db.Tenants.Where(t => t.Id == _superAdminTenantId).ExecuteDelete();
        db.TransitOffices.Where(o => o.Id == _office).ExecuteDelete();
    }
}
