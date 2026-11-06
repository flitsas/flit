using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Storage;
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
/// HU #14302 (Feature #14300, Épica #14271) — <c>GET /api/v1/tramites/biometric-validations/{id}/certificado</c> con el pipeline HTTP
/// real y PostgreSQL real: la carta de una validación MANUAL aprobada se descarga sin trámite. Super Admin de cualquier compañía
/// (200); usuario de OTRO tenant 404 (no revela que existe); sin permiso del módulo 403; anónimo 401; carta imposible de obtener
/// 503; no manual 404. El storage y el generador de la carta se sustituyen por dobles en memoria.
/// <para>Uso: <c>Authenticate("SuperAdmin", tenant, user)</c> y <c>await Get(validationId)</c>.</para>
/// </summary>
public sealed class CartaIdentidadPorValidacionEndpointTests
    : IClassFixture<CartaIdentidadPorValidacionEndpointTests.Host>, IDisposable
{
    public sealed class Host : HostPorClase
    {
        internal MemoryStorage Storage { get; } = new();
        internal SwitchableLetters Letters { get; } = new();

        protected override WebApplicationFactory<Program> Configurar(WebApplicationFactory<Program> factory) =>
            factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IAttachmentStorage>();
                s.AddSingleton<IAttachmentStorage>(Storage);
                s.RemoveAll<IIdentityLetterService>();
                s.AddSingleton<IIdentityLetterService>(Letters);
            }));
    }

    internal sealed class MemoryStorage : IAttachmentStorage
    {
        public Dictionary<string, byte[]> Blobs { get; } = new(StringComparer.Ordinal);

        public Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath) { }

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(Blobs.TryGetValue(storagePath, out var b) ? new MemoryStream(b) : null);

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }

    /// <summary>Generador de la carta: con <see cref="Fail"/> simula el storage caído; sin él no hace nada (la carta ya existe).</summary>
    internal sealed class SwitchableLetters : IIdentityLetterService
    {
        public bool Fail { get; set; }
        public int Calls { get; set; }

        public Task<IdentityLetterOutcome> EnsureForValidationAsync(Guid validationId, CancellationToken ct = default)
        {
            Calls++;
            return Fail
                ? throw new IdentityLetterGenerationException("storage caído")
                : Task.FromResult(IdentityLetterOutcome.AlreadyPresent);
        }
    }

    private static readonly SymmetricSecurityKey DummyKey = new(Encoding.UTF8.GetBytes(new string('k', 64)));
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly MemoryStorage _storage;
    private readonly SwitchableLetters _letters;

    private readonly Guid _dueno = Guid.NewGuid();
    private readonly Guid _otro = Guid.NewGuid();
    private readonly Guid _tenantSuperAdmin = Guid.NewGuid();
    private readonly List<Guid> _validaciones = [];
    private readonly List<Guid> _personas = [];

    public CartaIdentidadPorValidacionEndpointTests(Host host)
    {
        _storage = host.Storage;
        _letters = host.Letters;
        _letters.Fail = false;
        _letters.Calls = 0;
        _factory = host.Factory;
        _client = _factory.CreateClient();
        using var db = NewDb();
        db.Tenants.AddRange(
            NewTenant(_dueno, "Compania duena HU14302"),
            NewTenant(_otro, "Otra compania HU14302"),
            NewTenant(_tenantSuperAdmin, "Super admin HU14302"));
        db.SaveChanges();
    }

    [Fact]
    public async Task SuperAdmin_descarga_la_carta_de_cualquier_compania()
    {
        var id = await SeedAsync(conCarta: true);
        Authenticate("SuperAdmin", _tenantSuperAdmin, Guid.NewGuid());

        var response = await Get(id);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync(Ct)).Should().Be($"CARTA-{id:N}");
    }

    [Fact]
    public async Task Usuario_del_mismo_tenant_con_permiso_descarga_la_carta()
    {
        var id = await SeedAsync(conCarta: true);
        Authenticate("AdminCompany", _dueno, Guid.NewGuid(), "validaciones.read");

        var response = await Get(id);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Usuario_de_otro_tenant_recibe_404_aunque_tenga_permiso()
    {
        var id = await SeedAsync(conCarta: true);
        Authenticate("AdminCompany", _otro, Guid.NewGuid(), "validaciones.read");

        var response = await Get(id);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _letters.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Usuario_sin_permiso_del_modulo_recibe_403()
    {
        var id = await SeedAsync(conCarta: true);
        Authenticate("AdminCompany", _dueno, Guid.NewGuid());

        var response = await Get(id);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Anonimo_recibe_401()
    {
        var id = await SeedAsync(conCarta: true);

        (await Get(id)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Si_la_carta_no_se_puede_generar_responde_503_reintentable()
    {
        var id = await SeedAsync(conCarta: false);
        _letters.Fail = true;
        Authenticate("SuperAdmin", _tenantSuperAdmin, Guid.NewGuid());

        var response = await Get(id);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("carta de identidad").And.NotContain("proveedor");
        _letters.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Validacion_no_manual_o_inexistente_responde_404()
    {
        var kyverum = await SeedAsync(conCarta: false, provider: BiometricProviders.Kyverum);
        Authenticate("SuperAdmin", _tenantSuperAdmin, Guid.NewGuid());

        (await Get(kyverum)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Get(Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Infraestructura ─────────────────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> Get(Guid id)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/tramites/biometric-validations/{id}/certificado");
        return await _client.SendAsync(request, Ct);
    }

    private async Task<Guid> SeedAsync(bool conCarta, string provider = BiometricProviders.Manual)
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        var manual = provider == BiometricProviders.Manual;
        var documento = $"9{Random.Shared.NextInt64(10_000_000, 99_999_999)}";
        var persona = new Person
        {
            Id = Guid.NewGuid(), TenantId = _dueno, DocumentType = "CC", DocumentNumber = documento,
            FullName = "Persona de prueba", Email = "titular14302@example.test", PersonType = PersonTypes.Natural, CreatedAt = now,
        };
        db.Persons.Add(persona);
        await db.SaveChangesAsync(Ct);
        _personas.Add(persona.Id);

        var v = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(), TenantId = _dueno, ProcedureInstanceId = null, PersonId = persona.Id,
            Name = persona.FullName, DocumentType = "CC", DocumentNumber = documento, Email = persona.Email,
            RegisteredEmail = persona.Email, Status = BiometricEstados.Aprobado, Provider = provider,
            ApprovalOrigin = manual ? BiometricApprovalOrigins.Manual : BiometricApprovalOrigins.Automatica,
            KyverumVerificationId = manual ? null : $"kyv-{Guid.NewGuid():N}",
            TokenHash = Guid.NewGuid().ToString("N"), ExpiresAt = now.AddHours(1), CreatedAt = now.AddDays(-1),
            ValidatedAt = now.AddHours(-1), ValidUntil = now.AddDays(30),
            FacePhotoPath = manual ? "r/rostro" : null, IdFrontPhotoPath = manual ? "r/anverso" : null,
            IdBackPhotoPath = manual ? "r/reverso" : null, SignatureImagePath = manual ? "r/firma" : null,
            SignatureImageSha256 = manual ? new string('a', 64) : null,
            ConsentAt = manual ? now.AddHours(-2) : null, ConsentIp = manual ? "203.0.113.7" : null,
            ConsentTextVersion = manual ? "manual-ley1581-v2" : null,
            ManualActivatedAt = manual ? now.AddDays(-1) : null,
        };
        if (manual && conCarta)
        {
            var path = $"it14302/{v.Id:N}.pdf";
            _storage.Blobs[path] = Encoding.UTF8.GetBytes($"CARTA-{v.Id:N}");
            v.RegistrarCartaIdentidad("FL-AAAAAAAA-AAAAAA", new string('a', 64), new string('b', 64), path, now);
        }

        db.ProcedureInstanceBiometricValidations.Add(v);
        await db.SaveChangesAsync(Ct);
        _validaciones.Add(v.Id);
        return v.Id;
    }

    private void Authenticate(string role, Guid tenant, Guid user, params string[] permisos) =>
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintToken(role, tenant, user, permisos));

    private FlitDbContext NewDb() => _factory.Services.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();

    private static Tenant NewTenant(Guid id, string legalName) => new()
    {
        Id = id, Code = $"H14302-{Guid.NewGuid():N}"[..20], LegalName = legalName, TaxId = TestNit.Unique(),
        TenantType = "RENTING", IsGroupParent = false, IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
    };

    private static string MintToken(string role, Guid tenantId, Guid userId, string[] permisos)
    {
        var claims = new List<Claim>
        {
            new("sub", userId.ToString()), new("role", role), new("tenant_id", tenantId.ToString()),
        };
        claims.AddRange(permisos.Select(p => new Claim("permissions", p)));
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
        using var db = NewDb();
        db.ProcedureInstanceBiometricValidations.Where(v => _validaciones.Contains(v.Id)).ExecuteDelete();
        db.Persons.Where(p => _personas.Contains(p.Id)).ExecuteDelete();
        db.Tenants.Where(t => t.Id == _dueno || t.Id == _otro || t.Id == _tenantSuperAdmin).ExecuteDelete();
    }
}
