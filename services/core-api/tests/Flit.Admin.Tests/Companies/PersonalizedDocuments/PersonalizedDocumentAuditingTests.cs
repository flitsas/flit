using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Companies.PersonalizedDocuments;
using Flit.Admin.Application.Companies.PersonalizedDocuments.Activate;
using Flit.Admin.Application.Companies.PersonalizedDocuments.Confirm;
using Flit.Admin.Application.Companies.PersonalizedDocuments.Create;
using Flit.Admin.Application.Companies.PersonalizedDocuments.Deactivate;
using Flit.Admin.Domain.Companies.PersonalizedDocuments;
using Flit.Infrastructure.Documents;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.PersonalizedDocuments;

/// <summary>
/// HU #11320, AC1 — auditoría a nivel de aplicación de las cuatro operaciones sobre
/// <c>admin.company_personalized_documents</c> (carga, activación/confirm, reactivación y vuelta al
/// documento del sistema), complementando el trigger de BD (<c>trg_audit_log</c>). Sigue el patrón ya
/// usado por <c>AdminResetPasswordHandler</c>: <see cref="IAdminAuditWriter"/> inyectado en el handler,
/// con actor y resultado. Cubre también que el superadministrador de FLIT que atraviesa compañías queda
/// registrado con su propio id como actor, y que ningún registro lleva datos sensibles (contenido del
/// PDF, sha256, filename o URL firmada).
/// </summary>
public sealed class PersonalizedDocumentAuditingTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-2000-4000-8000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-2000-4000-8000-000000000002");
    private static readonly Guid ActorId = Guid.Parse("cccccccc-2000-4000-8000-000000000003");
    private static readonly Guid SuperAdminId = Guid.Parse("dddddddd-2000-4000-8000-000000000004");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------- Carga (Create) ----------

    [Fact]
    public async Task Create_Success_WritesAuditEntry_WithActorAndSuccessResult()
    {
        var dbName = NewDbName();
        await using var ctx = NewContext(dbName);
        SeedTenantApiChannel(ctx, TenantA);

        var auditWriter = Substitute.For<IAdminAuditWriter>();
        var storage = new FakeStorage();
        var repository = new CompanyPersonalizedDocumentRepository(ctx);
        var settingsRepository = new TenantSettingsRepository(ctx, NullAuditContextAccessor.Instance);
        var handler = new CreatePersonalizedDocumentVersionHandler(
            storage, repository, settingsRepository, auditWriter, NullAuditContextAccessor.Instance);

        var result = await handler.HandleAsync(new CreatePersonalizedDocumentVersionCommand
        {
            TenantId = TenantA,
            DocumentType = PersonalizedDocumentTypes.Mandato,
            Filename = "mandato.pdf",
            Sha256 = "deadbeef",
            SizeBytes = 1024,
            CreatedBy = ActorId,
        }, Ct);

        result.Outcome.Should().Be(CreatePersonalizedDocumentVersionOutcome.Created);

        await auditWriter.Received(1).WriteAsync(
            Arg.Is<AdminAuditEntry>(e =>
                e.TenantId == TenantA
                && e.Module == AuditVocabulary.Modules.Companies
                && e.EntityName == "personalized_document"
                && e.Operation == AuditVocabulary.Operations.Create
                && e.Result == AuditVocabulary.Results.Success
                && e.ActorUserId == ActorId
                && e.TargetEntityId == result.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_ChannelNotEnabled_WritesFailureAuditEntry_WithoutTouchingStorage()
    {
        var dbName = NewDbName();
        await using var ctx = NewContext(dbName);
        SeedFlitSmtpChannel(ctx, TenantA);

        var auditWriter = Substitute.For<IAdminAuditWriter>();
        var storage = new FakeStorage();
        var repository = new CompanyPersonalizedDocumentRepository(ctx);
        var settingsRepository = new TenantSettingsRepository(ctx, NullAuditContextAccessor.Instance);
        var handler = new CreatePersonalizedDocumentVersionHandler(
            storage, repository, settingsRepository, auditWriter, NullAuditContextAccessor.Instance);

        var result = await handler.HandleAsync(new CreatePersonalizedDocumentVersionCommand
        {
            TenantId = TenantA,
            DocumentType = PersonalizedDocumentTypes.Mandato,
            Filename = "mandato.pdf",
            Sha256 = "deadbeef",
            SizeBytes = 1024,
            CreatedBy = ActorId,
        }, Ct);

        result.Outcome.Should().Be(CreatePersonalizedDocumentVersionOutcome.ChannelNotEnabled);
        storage.CreateUploadCalls.Should().Be(0);

        await auditWriter.Received(1).WriteAsync(
            Arg.Is<AdminAuditEntry>(e =>
                e.Result == AuditVocabulary.Results.Failure
                && e.ErrorCode == "canal_no_habilitado"
                && e.ActorUserId == ActorId),
            Arg.Any<CancellationToken>());
    }

    // ---------- Activación (Confirm) ----------

    [Fact]
    public async Task Confirm_Success_WritesAuditEntry()
    {
        var dbName = NewDbName();
        Guid id;
        string storagePath;
        var bytes = ValidPdf();

        var declaredSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));

        await using (var seed = NewContext(dbName))
        {
            SeedTenantApiChannel(seed, TenantA);
            id = SeedPending(seed, TenantA, PersonalizedDocumentTypes.Mandato, version: 1, declaredSha256, out storagePath);
        }

        var auditWriter = Substitute.For<IAdminAuditWriter>();
        await using var ctx = NewContext(dbName);
        var storage = new FakeStorage();
        storage.Seed(storagePath, bytes);
        var repository = new CompanyPersonalizedDocumentRepository(ctx);
        var settingsRepository = new TenantSettingsRepository(ctx, NullAuditContextAccessor.Instance);
        var validator = new PdfIntegrityValidator(new PdfSharpDocumentInspector(NullLogger<PdfSharpDocumentInspector>.Instance));
        var handler = new ConfirmPersonalizedDocumentVersionHandler(
            storage, repository, settingsRepository, validator, auditWriter, NullAuditContextAccessor.Instance);

        var result = await handler.HandleAsync(new ConfirmPersonalizedDocumentVersionCommand
        {
            TenantId = TenantA,
            Id = id,
            ConfirmedBy = ActorId,
        }, Ct);

        result.Outcome.Should().Be(ConfirmPersonalizedDocumentVersionOutcome.Activated);

        await auditWriter.Received(1).WriteAsync(
            Arg.Is<AdminAuditEntry>(e =>
                e.Module == AuditVocabulary.Modules.Companies
                && e.Operation == AuditVocabulary.Operations.Confirm
                && e.Result == AuditVocabulary.Results.Success
                && e.ActorUserId == ActorId
                && e.TargetEntityId == id
                // Nunca sha256 ni contenido del PDF en el rastro.
                && e.OldValue == null
                && e.NewValue == null),
            Arg.Any<CancellationToken>());
    }

    // ---------- Reactivación (Activate) ----------

    [Fact]
    public async Task Activate_Success_WritesAuditEntry()
    {
        var dbName = NewDbName();
        Guid id;
        await using (var seed = NewContext(dbName))
        {
            SeedTenantApiChannel(seed, TenantA);
            id = SeedVersion(seed, TenantA, PersonalizedDocumentTypes.Mandato, version: 1, status: "historico", isActive: false);
        }

        var auditWriter = Substitute.For<IAdminAuditWriter>();
        await using var ctx = NewContext(dbName);
        var repository = new CompanyPersonalizedDocumentRepository(ctx);
        var settingsRepository = new TenantSettingsRepository(ctx, NullAuditContextAccessor.Instance);
        var handler = new ActivatePersonalizedDocumentVersionHandler(
            repository, settingsRepository, auditWriter, NullAuditContextAccessor.Instance);

        var result = await handler.HandleAsync(new ActivatePersonalizedDocumentVersionCommand
        {
            TenantId = TenantA,
            Id = id,
            ActivatedBy = ActorId,
        }, Ct);

        result.Outcome.Should().Be(ActivatePersonalizedDocumentVersionOutcome.Activated);

        await auditWriter.Received(1).WriteAsync(
            Arg.Is<AdminAuditEntry>(e =>
                e.Module == AuditVocabulary.Modules.Companies
                && e.Operation == AuditVocabulary.Operations.Activate
                && e.Result == AuditVocabulary.Results.Success
                && e.ActorUserId == ActorId
                && e.TargetEntityId == id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Activate_SuperAdmin_ForeignTenant_WritesAuditEntry_WithSuperAdminAsActor()
    {
        // El superadministrador de FLIT SÍ atraviesa compañías (HU #11320, criterio 3): la operación
        // queda registrada con él (SuperAdminId) como actor, sobre el tenant real del recurso (B), sin
        // importar cuál sea "su" tenant de origen.
        var dbName = NewDbName();
        Guid id;
        await using (var seed = NewContext(dbName))
        {
            SeedTenantApiChannel(seed, TenantB);
            id = SeedVersion(seed, TenantB, PersonalizedDocumentTypes.Mandato, version: 1, status: "historico", isActive: false);
        }

        var auditWriter = Substitute.For<IAdminAuditWriter>();
        await using var ctx = NewContext(dbName);
        var repository = new CompanyPersonalizedDocumentRepository(ctx);
        var settingsRepository = new TenantSettingsRepository(ctx, NullAuditContextAccessor.Instance);
        var handler = new ActivatePersonalizedDocumentVersionHandler(
            repository, settingsRepository, auditWriter, NullAuditContextAccessor.Instance);

        var result = await handler.HandleAsync(new ActivatePersonalizedDocumentVersionCommand
        {
            TenantId = TenantB,
            Id = id,
            ActivatedBy = SuperAdminId,
        }, Ct);

        result.Outcome.Should().Be(ActivatePersonalizedDocumentVersionOutcome.Activated);

        await auditWriter.Received(1).WriteAsync(
            Arg.Is<AdminAuditEntry>(e =>
                e.TenantId == TenantB
                && e.ActorUserId == SuperAdminId
                && e.Result == AuditVocabulary.Results.Success),
            Arg.Any<CancellationToken>());
    }

    // ---------- Vuelta al documento del sistema (Deactivate) ----------

    [Fact]
    public async Task Deactivate_Success_WritesAuditEntry_WithoutSensitiveData()
    {
        var dbName = NewDbName();
        await using (var seed = NewContext(dbName))
        {
            SeedTenantApiChannel(seed, TenantA);
            SeedVersion(seed, TenantA, PersonalizedDocumentTypes.Mandato, version: 1, status: "activo", isActive: true);
        }

        var auditWriter = Substitute.For<IAdminAuditWriter>();
        await using var ctx = NewContext(dbName);
        var repository = new CompanyPersonalizedDocumentRepository(ctx);
        var settingsRepository = new TenantSettingsRepository(ctx, NullAuditContextAccessor.Instance);
        var handler = new DeactivatePersonalizedDocumentHandler(
            repository, settingsRepository, auditWriter, NullAuditContextAccessor.Instance);

        var result = await handler.HandleAsync(new DeactivatePersonalizedDocumentCommand
        {
            TenantId = TenantA,
            DocumentType = PersonalizedDocumentTypes.Mandato,
            DeactivatedBy = ActorId,
        }, Ct);

        result.Outcome.Should().Be(DeactivatePersonalizedDocumentOutcome.Deactivated);

        await auditWriter.Received(1).WriteAsync(
            Arg.Is<AdminAuditEntry>(e =>
                e.Module == AuditVocabulary.Modules.Companies
                && e.Operation == AuditVocabulary.Operations.Deactivate
                && e.Result == AuditVocabulary.Results.Success
                && e.ActorUserId == ActorId
                // "mandato"/"tramite_virtual" no es dato sensible, pero de todos modos NO viaja en
                // Old/NewValue — nunca contenido del PDF, filename ni URL firmada.
                && e.OldValue == null
                && e.NewValue == null),
            Arg.Any<CancellationToken>());
    }

    // ---------- Helpers ----------

    private static string NewDbName() => $"flit-personalized-docs-audit-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);

    private static void SeedTenantApiChannel(FlitDbContext ctx, Guid tenantId) => SeedPolicy(ctx, tenantId, "tenant_api");

    private static void SeedFlitSmtpChannel(FlitDbContext ctx, Guid tenantId) => SeedPolicy(ctx, tenantId, "flit_smtp");

    private static void SeedPolicy(FlitDbContext ctx, Guid tenantId, string channel)
    {
        ctx.TenantOperationalPolicies.Add(new TenantOperationalPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            NotificationChannel = channel,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
    }

    private static Guid SeedVersion(
        FlitDbContext ctx, Guid tenantId, string documentType, int version, string status, bool isActive)
    {
        var id = Guid.NewGuid();
        ctx.CompanyPersonalizedDocuments.Add(new CompanyPersonalizedDocumentEntity
        {
            Id = id,
            TenantId = tenantId,
            DocumentType = documentType,
            Version = version,
            Status = status,
            IsActive = isActive,
            Filename = $"{documentType}.pdf",
            StoragePath = $"path/{id}.pdf",
            StorageSha256 = "sha-seed",
            SizeBytes = 100,
            PageCount = 3,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
        return id;
    }

    private static Guid SeedPending(
        FlitDbContext ctx, Guid tenantId, string documentType, int version, string declaredSha256, out string storagePath)
    {
        var id = Guid.NewGuid();
        storagePath = $"path/{id}.pdf";
        ctx.CompanyPersonalizedDocuments.Add(new CompanyPersonalizedDocumentEntity
        {
            Id = id,
            TenantId = tenantId,
            DocumentType = documentType,
            Version = version,
            Status = "pendiente",
            IsActive = false,
            Filename = $"{documentType}.pdf",
            StoragePath = storagePath,
            StorageSha256 = declaredSha256,
            SizeBytes = 100,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
        return id;
    }

    private static byte[] ValidPdf()
    {
        using var document = new PdfSharpCore.Pdf.PdfDocument();
        document.AddPage();
        using var stream = new MemoryStream();
        document.Save(stream, closeStream: false);
        return stream.ToArray();
    }

    /// <summary>Storage en memoria: no toca red; guarda bytes por <c>storagePath</c> para el confirm.</summary>
    private sealed class FakeStorage : ICompanyPersonalizedDocumentStorage
    {
        private readonly Dictionary<string, byte[]> _objects = [];
        private int _sequence;

        public int CreateUploadCalls { get; private set; }

        public void Seed(string storagePath, byte[] bytes) => _objects[storagePath] = bytes;

        public Task<PersonalizedDocumentUploadTicket> CreateUploadAsync(
            Guid tenantId, string documentType, CancellationToken cancellationToken = default)
        {
            CreateUploadCalls++;
            var storagePath = $"fm-personalized-{tenantId}-{documentType}-{++_sequence}";
            return Task.FromResult(new PersonalizedDocumentUploadTicket(
                storagePath, "https://s3.example/upload", new Dictionary<string, string> { ["key"] = storagePath }));
        }

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
        {
            if (!_objects.TryGetValue(storagePath, out var bytes))
            {
                return Task.FromResult<Stream?>(null);
            }

            return Task.FromResult<Stream?>(new MemoryStream(bytes, writable: false));
        }

        public Task<PersonalizedDocumentView?> GetViewUrlAsync(string storagePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<PersonalizedDocumentView?>(null);
    }
}
