using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Platform;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Modules.Platform.Domain.TenantProducts;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.Platform;

/// <summary>
/// HU #14711 (AC-DC-01, AC-DC-02, AC-DC-05, AC-DC-06, AC-DC-12) — <c>POST /api/v1/platform/admin/tenants/{tenantId}/products/
/// diagnostico/credentials/impuesto/obtener</c> contra la API real (WebApplicationFactory + base real) con el proveedor
/// reemplazado por un doble: 200 con el JSON exacto (solo metadatos), errores RFC 7807 con <c>code</c> (422/409/404/502/503),
/// solo SuperAdmin, límite por empresa, el PUT manual no acepta los códigos del webhook y el listado dice si hay secreto.
/// </summary>
/// <remarks>
/// Uso de ejemplo: <c>POST …/credentials/impuesto/obtener</c> sin cuerpo →
/// <c>{ "apiKeyPrefix": "AbCdEfGh", "created": true, "rotated": false, "hasWebhookSecret": true, "changed": true, "updatedAt": "…" }</c>.
/// </remarks>
public sealed class ObtenerCredencialImpuestoDiagnosticoEndpointTests
    : IClassFixture<ObtenerCredencialImpuestoDiagnosticoEndpointTests.Fabrica>, IDisposable
{
    private const string Llave = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_abcde";
    private const string Secreto = "whsec_CENTINELA_endpoint_0123456789";
    private static readonly SymmetricSecurityKey DummyKey = new(Encoding.UTF8.GetBytes(new string('k', 64)));

    private readonly Fabrica _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _superAdminId = Guid.NewGuid();
    private readonly Guid _adminId = Guid.NewGuid();
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..10];

    public ObtenerCredencialImpuestoDiagnosticoEndpointTests(Fabrica factory)
    {
        _factory = factory;
        _factory.Proveedor.Responder = _ => new RespuestaAltaProveedor(Llave, Secreto, Created: true, ApiKeyRotated: false);
        _client = factory.CreateClient();
        SeedAsync(TestNit.Unique() + "-7").GetAwaiter().GetResult();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Ruta(Guid? tenant = null) =>
        $"/api/v1/platform/admin/tenants/{tenant ?? _tenantId}/products/diagnostico/credentials/impuesto/obtener";

    [Fact]
    public async Task Obtener_200_ConElJsonExacto_SoloMetadatos_YLaCredencialQuedaGuardada()
    {
        await EncenderDiagnosticoAsync();
        Use("SuperAdmin", _superAdminId);

        var r = await _client.PostAsync(Ruta(), null, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var texto = await r.Content.ReadAsStringAsync(Ct);
        texto.Should().NotContain(Llave).And.NotContain(Secreto);
        using var json = JsonDocument.Parse(texto);
        json.RootElement.EnumerateObject().Select(p => p.Name)
            .Should().Equal("apiKeyPrefix", "created", "rotated", "hasWebhookSecret", "changed", "updatedAt");
        json.RootElement.GetProperty("apiKeyPrefix").GetString().Should().Be("AbCdEfGh");
        json.RootElement.GetProperty("created").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("rotated").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("hasWebhookSecret").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("changed").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("updatedAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));

        await using var db = CreateDbContext();
        (await db.Set<TenantProductCredentialEntity>().AsNoTracking().Where(c => c.TenantId == _tenantId).Select(c => c.CredentialCode).ToListAsync(Ct))
            .Should().BeEquivalentTo("impuesto_api_key", "impuesto_webhook_secret");
        (await db.Set<TenantProductProvisioningEntity>().AsNoTracking().SingleAsync(p => p.TenantId == _tenantId, Ct)).Status.Should().Be("LISTO");
        _factory.Proveedor.Ultima.Should().NotBeNull();
        _factory.Proveedor.Ultima!.ContactEmail.Should().Be($"alta-admin-{_suffix}@flit.local");
    }

    [Fact]
    public async Task Obtener_DosVeces_ConLosMismosValores_LaSegundaNoCambiaNada()
    {
        await EncenderDiagnosticoAsync();
        Use("SuperAdmin", _superAdminId);
        (await _client.PostAsync(Ruta(), null, Ct)).EnsureSuccessStatusCode();
        _factory.Proveedor.Responder = _ => new RespuestaAltaProveedor(Llave, Secreto, Created: false, ApiKeyRotated: false);

        var r = await _client.PostAsync(Ruta(), null, Ct);

        var json = await r.Content.ReadFromJsonAsync<JsonElement>(Ct);
        json.GetProperty("changed").GetBoolean().Should().BeFalse();
        json.GetProperty("created").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Obtener_NitAmbiguo_422_ConCodigo()
    {
        await using (var db = CreateDbContext())
        {
            await db.Tenants.Where(t => t.Id == _tenantId).ExecuteUpdateAsync(s => s.SetProperty(t => t.TaxId, TestNit.Unique()), Ct);
        }

        await EncenderDiagnosticoAsync();
        Use("SuperAdmin", _superAdminId);

        var r = await _client.PostAsync(Ruta(), null, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ProblemaAsync(r)).Should().Be(("NIT_AMBIGUO", CodigosAltaCredencialImpuesto.Literal("NIT_AMBIGUO")));
    }

    [Fact]
    public async Task Obtener_DiagnosticoApagado_409_ProductoNoHabilitado()
    {
        Use("SuperAdmin", _superAdminId);

        var r = await _client.PostAsync(Ruta(), null, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemaAsync(r)).Code.Should().Be("PRODUCTO_NO_HABILITADO");
    }

    [Theory]
    [InlineData("PROVEEDOR_TOKEN_INVALIDO", 502)]
    [InlineData("PROVEEDOR_INTEGRACION_DESHABILITADA", 502)]
    [InlineData("PROVEEDOR_SOLICITUD_INVALIDA", 502)]
    [InlineData("PROVEEDOR_NO_DISPONIBLE", 502)]
    [InlineData("PROVEEDOR_NO_CONFIGURADO", 503)]
    public async Task Obtener_ErrorDelProveedor_ConSuEstadoYCodigo(string codigo, int status)
    {
        await EncenderDiagnosticoAsync();
        _factory.Proveedor.Responder = _ => throw new AltaCredencialImpuestoException(codigo);
        Use("SuperAdmin", _superAdminId);

        var r = await _client.PostAsync(Ruta(), null, Ct);

        ((int)r.StatusCode).Should().Be(status);
        (await ProblemaAsync(r)).Should().Be((codigo, CodigosAltaCredencialImpuesto.Literal(codigo)));
    }

    [Fact]
    public async Task Obtener_EmpresaInexistente_404()
    {
        Use("SuperAdmin", _superAdminId);

        var r = await _client.PostAsync(Ruta(Guid.NewGuid()), null, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ProblemaAsync(r)).Code.Should().Be("TENANT_NOT_FOUND");
    }

    [Fact]
    public async Task Obtener_SoloSuperAdmin()
    {
        await EncenderDiagnosticoAsync();
        Use("AdminCompany", _adminId);

        var r = await _client.PostAsync(Ruta(), null, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Proveedor.Ultima.Should().BeNull();
    }

    [Fact]
    public async Task Obtener_LimiteDeCincoPorMinutoPorEmpresa()
    {
        await EncenderDiagnosticoAsync();
        Use("SuperAdmin", _superAdminId);

        var estados = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
            estados.Add((await _client.PostAsync(Ruta(), null, Ct)).StatusCode);

        estados.Take(5).Should().AllBeEquivalentTo(HttpStatusCode.OK);
        estados[5].Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Obtener_UnaPeticionAnonima_NoConsumeElCupoDelSuperAdmin()
    {
        await EncenderDiagnosticoAsync();
        _client.DefaultRequestHeaders.Authorization = null;
        for (var i = 0; i < 6; i++)
            (await _client.PostAsync(Ruta(), null, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        Use("SuperAdmin", _superAdminId);
        var estados = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
            estados.Add((await _client.PostAsync(Ruta(), null, Ct)).StatusCode);

        estados.Take(5).Should().AllBeEquivalentTo(HttpStatusCode.OK);
        estados[5].Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Obtener_ChocaConLaRondaQueGuardaOtraLlave_409AltaEnCurso_NuncaUn500()
    {
        await EncenderDiagnosticoAsync();
        Use("SuperAdmin", _superAdminId);
        // La ronda guarda otra llave entre la lectura y el guardado del botón (el gancho corre dentro del guardado, antes del
        // SaveChanges): el insert del botón choca con el unique.
        _factory.AntesDelGuardado = () =>
        {
            _factory.AntesDelGuardado = null;
            using var scope = _factory.Services.CreateScope();
            var protector = scope.ServiceProvider.GetRequiredService<ITenantProductSecretProtector>();
            var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
            db.Set<TenantProductCredentialEntity>().Add(new TenantProductCredentialEntity
            {
                Id = Guid.CreateVersion7(), TenantId = _tenantId, ProductCode = "diagnostico", CredentialCode = "impuesto_api_key",
                SecretProtected = protector.Protect(_tenantId, "impuesto_api_key", "OtraLlaveDeLaRonda-0123456789-abcdefgh"),
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            });
            db.SaveChanges();
        };

        var r = await _client.PostAsync(Ruta(), null, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemaAsync(r)).Should().Be(("ALTA_EN_CURSO", "Ya hay un alta de la credencial del impuesto en curso para la compañía. Intenta de nuevo en unos segundos."));
    }

    [Theory]
    [InlineData("impuesto_webhook_secret")]
    [InlineData("impuesto_webhook_secret_anterior")]
    public async Task PutManual_NoAceptaLosCodigosDelWebhook(string codigo)
    {
        Use("SuperAdmin", _superAdminId);

        var r = await _client.PutAsJsonAsync(
            $"/api/v1/platform/admin/tenants/{_tenantId}/products/diagnostico/credentials/{codigo}", new { value = Secreto }, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ProblemaAsync(r)).Code.Should().Be("CREDENTIAL_NOT_SUPPORTED");
    }

    [Fact]
    public async Task Listado_DiceSiElSecretoDelWebhookEstaConfigurado()
    {
        await EncenderDiagnosticoAsync();
        Use("SuperAdmin", _superAdminId);
        (await _client.PostAsync(Ruta(), null, Ct)).EnsureSuccessStatusCode();

        var list = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/platform/admin/tenants/{_tenantId}/products", Ct);

        var diagnostico = list.EnumerateArray().Single(p => p.GetProperty("productCode").GetString() == "diagnostico");
        diagnostico.GetProperty("impuestoWebhookSecretConfigurada").GetBoolean().Should().BeTrue();
        diagnostico.GetProperty("impuestoApiKeyConfigurada").GetBoolean().Should().BeTrue();
        diagnostico.GetProperty("provisioning").GetProperty("status").GetString().Should().Be("LISTO");
    }

    // ── Montaje ─────────────────────────────────────────────────────────────────────────────────────────────

    private async Task EncenderDiagnosticoAsync()
    {
        Use("SuperAdmin", _superAdminId);
        (await _client.PutAsJsonAsync($"/api/v1/platform/admin/tenants/{_tenantId}/products/diagnostico", new { enabled = true }, Ct))
            .EnsureSuccessStatusCode();
    }

    private static async Task<(string? Code, string? Detail)> ProblemaAsync(HttpResponseMessage r)
    {
        var json = await r.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return (json.TryGetProperty("code", out var c) ? c.GetString() : null, json.TryGetProperty("detail", out var d) ? d.GetString() : null);
    }

    private void Use(string role, Guid userId) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity([new Claim("sub", userId.ToString()), new Claim("role", role), new Claim("tenant_id", _tenantId.ToString())]),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(DummyKey, SecurityAlgorithms.HmacSha256),
        }));

    private FlitDbContext CreateDbContext() => _factory.Services.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();

    private async Task SeedAsync(string taxId)
    {
        await using var db = CreateDbContext();
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId, Code = $"IT-14711-{_suffix}", LegalName = "Flota Andina SAS", TaxId = taxId,
            TenantType = "RENTING", IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
        });
        db.Users.Add(new User
        {
            Id = _superAdminId, Email = $"alta-sa-{_suffix}@flit.local", DisplayName = "SuperAdmin 14711", Status = "active",
            HomeTenantId = _tenantId, CreatedAt = DateTimeOffset.UtcNow,
        });
        db.Users.Add(new User
        {
            Id = _adminId, Email = $"alta-admin-{_suffix}@flit.local", DisplayName = "Admin 14711", Status = "active",
            HomeTenantId = _tenantId, CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(Ct);

        var adminCompany = await db.Roles.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Code == "AdminCompany" && r.TargetEntityType == "COMPANY" && r.DeletedAt == null, Ct);
        if (adminCompany is null)
        {
            adminCompany = new Role
            {
                Id = Guid.CreateVersion7(), Code = "AdminCompany", Name = "Administrador de Compañía", TargetEntityType = "COMPANY",
                ProductCode = "plataforma", IsSystem = true, IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Roles.Add(adminCompany);
            await db.SaveChangesAsync(Ct);
        }

        db.UserRoleAssignments.Add(new UserRoleAssignment
        {
            Id = Guid.CreateVersion7(), TenantId = _tenantId, UserId = _adminId, RoleId = adminCompany.Id,
            AssignedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(Ct);
    }

    public void Dispose()
    {
        using var db = CreateDbContext();
        db.UserRoleAssignments.Where(a => a.TenantId == _tenantId).ExecuteDelete();
        db.Set<TenantProductCredentialEntity>().Where(c => c.TenantId == _tenantId).ExecuteDelete();
        db.Set<TenantProductProvisioningEntity>().Where(c => c.TenantId == _tenantId).ExecuteDelete();
        db.Set<TenantProductEntity>().Where(r => r.TenantId == _tenantId).ExecuteDelete();
        db.TenantConfigAuditLogs.Where(a => a.TenantId == _tenantId).ExecuteDelete();
        db.Users.Where(u => u.Id == _superAdminId || u.Id == _adminId).ExecuteDelete();
        db.Tenants.Where(t => t.Id == _tenantId).ExecuteDelete();
        _factory.Proveedor.Ultima = null;
        _factory.AntesDelGuardado = null;
        _client.Dispose();
    }

    /// <summary>La API real con el proveedor del alta reemplazado por un doble mutable por prueba.</summary>
    public sealed class Fabrica : WebApplicationFactory<Program>
    {
        public ProveedorDePrueba Proveedor { get; } = new();

        /// <summary>Si se da, corre al encolar el aviso a Impuestos: dentro del guardado del alta, antes del SaveChanges.</summary>
        public Action? AntesDelGuardado { get; set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureTestServices(s =>
            {
                s.AddSingleton<IClienteAltaProveedorImpuesto>(Proveedor);
                var original = s.Last(d => d.ServiceType == typeof(ITenantProductChangeJobs));
                s.Remove(original);
                s.Add(ServiceDescriptor.Describe(
                    typeof(ITenantProductChangeJobs),
                    sp => new JobsConGancho(
                        (ITenantProductChangeJobs)(original.ImplementationFactory?.Invoke(sp)
                            ?? original.ImplementationInstance
                            ?? ActivatorUtilities.CreateInstance(sp, original.ImplementationType!)),
                        this),
                    original.Lifetime));
            });
    }

    /// <summary>Los trabajos reales, con <see cref="Fabrica.AntesDelGuardado"/> al encolar el aviso de la llave.</summary>
    private sealed class JobsConGancho(ITenantProductChangeJobs real, Fabrica fabrica) : ITenantProductChangeJobs
    {
        public void EncolarCambio(TenantProduct actual) => real.EncolarCambio(actual);

        public void EncolarCredencialDiagnosticoActualizada(Guid tenantId, long version, DateTimeOffset occurredAt)
        {
            fabrica.AntesDelGuardado?.Invoke();
            real.EncolarCredencialDiagnosticoActualizada(tenantId, version, occurredAt);
        }

        public Task EncolarReintentoAsync(Guid tenantId, string productCode, Guid solicitadoPor, CancellationToken cancellationToken = default) =>
            real.EncolarReintentoAsync(tenantId, productCode, solicitadoPor, cancellationToken);
    }

    /// <summary>Doble del proveedor: responde con <see cref="Responder"/> y recuerda la última petición.</summary>
    public sealed class ProveedorDePrueba : IClienteAltaProveedorImpuesto
    {
        public Func<PeticionAltaProveedor, RespuestaAltaProveedor> Responder { get; set; } = _ => throw new InvalidOperationException("sin respuesta");

        public PeticionAltaProveedor? Ultima { get; set; }

        public Task<RespuestaAltaProveedor> DarDeAltaAsync(PeticionAltaProveedor peticion, CancellationToken cancellationToken = default)
        {
            Ultima = peticion;
            return Task.FromResult(Responder(peticion));
        }
    }
}
