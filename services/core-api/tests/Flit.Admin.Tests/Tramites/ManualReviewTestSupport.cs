using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
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
/// Soporte común de las pruebas HTTP de la revisión manual de identidad (Feature #13282 C, Épica #13202) contra PostgreSQL real:
/// host con el storage en memoria (lectura incluida) y el notificador de correo simulado, siembra de validaciones manuales
/// (token_hash ÚNICO por siembra y filas Kyverum en vuelo con vencimiento a futuro: el worker del host expira o reconcilia las
/// vencidas en mitad de la prueba), JWT por rol y limpieza.
/// <para>Uso: <c>public sealed class XTests : IClassFixture&lt;ManualReviewFactory&gt;, IDisposable</c> con
/// <c>new ManualReviewHost(factory)</c>.</para>
/// </summary>
public sealed class ManualReviewFactory : WebApplicationFactory<Program>
{
    public ReadableMemoryStorage Storage { get; } = new();

    public CapturingManualNotifier Notifier { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAttachmentStorage>();
            services.AddSingleton<IAttachmentStorage>(Storage);
            services.RemoveAll<IManualCaptureLinkNotifier>();
            services.AddSingleton<IManualCaptureLinkNotifier>(Notifier);
        });
    }
}

/// <summary>Notificador simulado: registra los enlaces recibidos (token en claro) en lugar de enviar correo real.</summary>
public sealed class CapturingManualNotifier : IManualCaptureLinkNotifier
{
    private readonly object _gate = new();
    private readonly List<ManualCaptureLink> _enviados = [];

    public IReadOnlyList<ManualCaptureLink> Enviados
    {
        get
        {
            lock (_gate)
            {
                return [.. _enviados];
            }
        }
    }

    public bool Resultado { get; set; } = true;

    public void Clear()
    {
        lock (_gate)
        {
            _enviados.Clear();
        }

        Resultado = true;
    }

    public Task<bool> NotifyAsync(ManualCaptureLink link, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _enviados.Add(link);
        }

        return Task.FromResult(Resultado);
    }
}

/// <summary>Storage de adjuntos en memoria que también sirve lectura (la prueba de imágenes lee lo que sembró).</summary>
public sealed class ReadableMemoryStorage : IAttachmentStorage
{
    private readonly object _gate = new();
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public string Put(byte[] bytes)
    {
        var path = $"fm-{Guid.NewGuid():N}";
        lock (_gate)
        {
            _files[path] = bytes;
        }

        return path;
    }

    public Task<StoredFile> SaveAsync(Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        content.CopyTo(ms);
        var bytes = ms.ToArray();
        return Task.FromResult(new StoredFile(Put(bytes), Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.LongLength));
    }

    public void Delete(string storagePath)
    {
        lock (_gate)
        {
            _files.Remove(storagePath);
        }
    }

    public Task<PresignedUpload> CreatePresignedUploadAsync(Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult<Stream?>(_files.TryGetValue(storagePath, out var bytes) ? new MemoryStream(bytes, writable: false) : null);
        }
    }

    public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(string storagePath, CancellationToken ct = default) =>
        throw new NotSupportedException();
}

/// <summary>Siembra, JWT y limpieza de una clase de pruebas HTTP de la revisión manual.</summary>
public sealed class ManualReviewHost : IDisposable
{
    private static readonly SymmetricSecurityKey DummyKey = new(Encoding.UTF8.GetBytes(new string('k', 64)));

    /// <summary>JPEG mínimo válido por sus magic bytes (lo único que mira la entrega de imágenes).</summary>
    public static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];

    /// <summary>PNG mínimo por sus magic bytes.</summary>
    public static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    private readonly List<Guid> _validaciones = [];
    private readonly List<Guid> _personas = [];
    private readonly List<Guid> _tenants = [];

    public ManualReviewHost(ManualReviewFactory factory)
    {
        Factory = factory;
        Client = factory.CreateClient();
        Factory.Notifier.Clear();
        Dueno = NewTenant("Compania duena C");
        TenantSuperAdmin = NewTenant("Super admin C");
    }

    public ManualReviewFactory Factory { get; }

    public HttpClient Client { get; }

    public Guid Dueno { get; }

    public Guid TenantSuperAdmin { get; }

    public Guid SuperAdmin { get; } = Guid.NewGuid();

    public Guid AdminDueno { get; } = Guid.NewGuid();

    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public FlitDbContext NewDb() => Factory.Services.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();

    public void Authenticate(string role, Guid tenant, Guid user) =>
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintToken(role, tenant, user));

    public void AuthenticateSuperAdmin() => Authenticate("SuperAdmin", TenantSuperAdmin, SuperAdmin);

    public void AuthenticateAdminCompany() => Authenticate("AdminCompany", Dueno, AdminDueno);

    public void Anonymous() => Client.DefaultRequestHeaders.Authorization = null;

    /// <summary>
    /// Siembra una validación (persona + fila) en la compañía dueña. Con <paramref name="conImagenes"/> guarda 4 archivos en el
    /// storage en memoria y deja sus rutas en la fila; <paramref name="tokenHashExterno"/> permite fijar el hash.
    /// </summary>
    public async Task<Seeded> SeedAsync(
        string status = BiometricEstados.PendienteRevisionManual,
        string provider = BiometricProviders.Manual,
        bool conImagenes = true,
        bool consentimientoVigente = true,
        string? email = "titular13297@example.test",
        string? approvalOrigin = null,
        string? rejectionReasonCode = null,
        Guid? reviewedBy = null,
        Guid? procedureInstanceId = null)
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        var documento = $"9{Random.Shared.NextInt64(10_000_000, 99_999_999)}";
        var persona = new Person
        {
            Id = Guid.NewGuid(), TenantId = Dueno, DocumentType = "CC", DocumentNumber = documento,
            FullName = "Persona de prueba", Email = email ?? string.Empty, PersonType = PersonTypes.Natural, CreatedAt = now,
        };
        db.Persons.Add(persona);
        await db.SaveChangesAsync(Ct);
        _personas.Add(persona.Id);

        var esManual = provider == BiometricProviders.Manual;
        var hash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var v = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(), TenantId = Dueno, ProcedureInstanceId = procedureInstanceId, PersonId = persona.Id,
            Name = persona.FullName, DocumentType = "CC", DocumentNumber = documento, Email = persona.Email,
            RegisteredEmail = persona.Email, Status = status, Provider = provider, TokenHash = hash,
            // Una fila Kyverum en vuelo con vencimiento pasado la expira o reconcilia el worker del host en mitad de la prueba.
            ExpiresAt = now.AddHours(esManual ? 5 : 1), CreatedAt = now.AddDays(-2),
            ManualActivatedBy = esManual ? SuperAdmin : null,
            ManualActivatedAt = esManual ? now.AddHours(-3) : null,
            ConsentAt = esManual && consentimientoVigente ? now.AddHours(-2) : null,
            ConsentTextVersion = esManual && consentimientoVigente ? "v1" : null,
            ConsentIp = esManual && consentimientoVigente ? "203.0.113.7" : null,
            ApprovalOrigin = approvalOrigin,
            RejectionReasonCode = rejectionReasonCode,
            ReviewedBy = reviewedBy,
            UpdatedAt = now.AddHours(-3),
            ReviewedAt = reviewedBy is null ? null : now.AddHours(-1),
        };

        var paths = new Dictionary<string, string>();
        if (conImagenes)
        {
            paths["rostro"] = v.FacePhotoPath = Factory.Storage.Put(JpegBytes);
            paths["anverso"] = v.IdFrontPhotoPath = Factory.Storage.Put(JpegBytes);
            paths["reverso"] = v.IdBackPhotoPath = Factory.Storage.Put(JpegBytes);
            paths["firma"] = v.SignatureImagePath = Factory.Storage.Put(PngBytes);
            v.SignatureImageSha256 = new string('a', 64);
        }

        db.ProcedureInstanceBiometricValidations.Add(v);
        await db.SaveChangesAsync(Ct);
        _validaciones.Add(v.Id);
        return new Seeded(v.Id, hash, documento, paths);
    }

    public Task<HttpResponseMessage> GetAsync(string url) => Client.GetAsync(url, Ct);

    public Task<HttpResponseMessage> PostAsync(string url, HttpContent? content = null) => Client.PostAsync(url, content, Ct);

    public async Task<ProcedureInstanceBiometricValidation> ReloadAsync(Guid id)
    {
        await using var db = NewDb();
        return await db.ProcedureInstanceBiometricValidations.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
    }

    /// <summary>Siembra el evento <c>manual_captura_recibida</c> de una validación hace <paramref name="minutosAtras"/> minutos.</summary>
    public async Task SeedCapturaRecibidaAsync(Guid id, int minutosAtras)
    {
        await using var db = NewDb();
        var at = DateTimeOffset.UtcNow.AddMinutes(-minutosAtras);
        db.IdentityValidationAudits.Add(new IdentityValidationAuditEvent
        {
            Id = Guid.NewGuid(), OccurredAt = at, CreatedAt = at, Stage = IdentityValidationAuditStages.ManualCapturaRecibida,
            Outcome = IdentityValidationAuditOutcomes.Ok, ValidationId = id, TenantId = Dueno,
        });
        await db.SaveChangesAsync(Ct);
    }

    public async Task<List<IdentityValidationAuditEvent>> AuditAsync(Guid id)
    {
        await using var db = NewDb();
        return await db.IdentityValidationAudits.AsNoTracking().Where(a => a.ValidationId == id).OrderBy(a => a.OccurredAt).ToListAsync(Ct);
    }

    private Guid NewTenant(string legalName)
    {
        var id = Guid.NewGuid();
        using var db = NewDb();
        db.Tenants.Add(new Tenant
        {
            Id = id, Code = $"H13297-{Guid.NewGuid():N}"[..20], LegalName = legalName, TaxId = TestNit.Unique(),
            TenantType = "RENTING", IsGroupParent = false, IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
        _tenants.Add(id);
        return id;
    }

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
        db.IdentityValidationAudits.Where(a => a.ValidationId != null && _validaciones.Contains(a.ValidationId.Value)).ExecuteDelete();
        db.IdentityValidationOutbox.Where(o => _validaciones.Contains(o.ValidationId)).ExecuteDelete();
        db.ProcedureInstanceBiometricValidations.Where(v => _validaciones.Contains(v.Id)).ExecuteDelete();
        db.Persons.Where(p => _personas.Contains(p.Id)).ExecuteDelete();
        db.Tenants.Where(t => _tenants.Contains(t.Id)).ExecuteDelete();
        Client.Dispose();
    }

    /// <summary>Validación sembrada: id, hash del token, documento y rutas de storage por tipo de imagen.</summary>
    public sealed record Seeded(Guid Id, string TokenHash, string Documento, IReadOnlyDictionary<string, string> Paths);
}
