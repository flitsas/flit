using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Companies.PersonalizedDocuments;
using Flit.Admin.Application.Companies.PersonalizedDocuments.Confirm;
using Flit.Admin.Application.Companies.PersonalizedDocuments.Create;
using Flit.Admin.Application.Companies.PersonalizedDocuments.List;
using Flit.Admin.Domain.Companies.PersonalizedDocuments;
using Flit.Infrastructure.Documents;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PdfSharpCore.Pdf;
using Xunit;

namespace Flit.Admin.Tests.Companies.PersonalizedDocuments;

/// <summary>
/// Tests de documentos personalizados por compañía (HU #11313, Feature #11309, ADR-0042). Ejercitan
/// los handlers reales (<see cref="CreatePersonalizedDocumentVersionHandler"/>,
/// <see cref="ConfirmPersonalizedDocumentVersionHandler"/>, <see cref="ListPersonalizedDocumentsHandler"/>)
/// sobre <see cref="CompanyPersonalizedDocumentRepository"/> + <see cref="TenantSettingsRepository"/>
/// reales (EF InMemory) y un storage en memoria (sin red). Cubren los 5 escenarios de aceptación de la
/// HU más el negativo de aislamiento cross-tenant (§8 del plan técnico):
///
/// AC1 — solo <c>mandato</c>/<c>tramite_virtual</c> admitidos, el resto 400.
/// AC2 — cada carga crea versión; ninguna borra la anterior (pasa a histórico, archivo intacto).
/// AC3 — el confirm rechaza (422) y deja la versión en <c>rechazado</c> ante mime falso, magic byte
///       ausente, cifrado, PDF ilegible, tamaño/páginas excedidos o hash no coincidente.
/// AC4 — con canal <c>FLIT_SMTP</c> las rutas de escritura responden 409 <c>canal_no_habilitado</c>.
/// AC5 — negativo de aislamiento: un administrador de la compañía A no lee/confirma lo de la compañía B.
/// </summary>
public sealed class PersonalizedDocumentHandlerTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
    private static readonly Guid ActorId = Guid.Parse("cccccccc-0000-4000-8000-000000000003");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------- AC1: vocabulario cerrado del tipo de documento ----------

    [Fact]
    public async Task AC1_InvalidDocumentType_Returns400AndDoesNotTouchStorageOrRepository()
    {
        await using var ctx = NewContext(TenantApiChannel(TenantA));
        var (create, _, storage) = Handlers(ctx);

        var result = await create.HandleAsync(NewCreateCommand(documentType: "escritura"), Ct);

        result.Outcome.Should().Be(CreatePersonalizedDocumentVersionOutcome.InvalidDocumentType);
        result.Errors.Should().ContainSingle(e => e.Code == "tipo_documento_invalido");
        storage.CreateUploadCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(PersonalizedDocumentTypes.Mandato)]
    [InlineData(PersonalizedDocumentTypes.TramiteVirtual)]
    public async Task AC1_ValidDocumentTypes_AreAccepted(string documentType)
    {
        await using var ctx = NewContext(TenantApiChannel(TenantA));
        var (create, _, _) = Handlers(ctx);

        var result = await create.HandleAsync(NewCreateCommand(documentType: documentType), Ct);

        result.Outcome.Should().Be(CreatePersonalizedDocumentVersionOutcome.Created);
    }

    // ---------- AC2: historial versionado, ninguna versión se borra ----------

    [Fact]
    public async Task AC2_EachUpload_CreatesNewVersion_PreviousBecomesHistoricoWithFileIntact()
    {
        var dbName = NewDbName();

        await using (var seed = NewContext(dbName))
        {
            SeedTenantApiChannel(seed, TenantA);
        }

        // Primera versión: alta + confirm → activa (v1).
        Guid firstId;
        string firstStoragePath;
        await using (var ctx1 = NewContext(dbName))
        {
            var (create, confirm, storage) = Handlers(ctx1);
            var firstPdf = ValidPdf(pages: 1);
            var created = await create.HandleAsync(NewCreateCommand(sha256: ToSha256(firstPdf)), Ct);
            created.Outcome.Should().Be(CreatePersonalizedDocumentVersionOutcome.Created);
            created.Version.Should().Be(1);
            firstId = created.Id!.Value;
            firstStoragePath = created.Upload!.StoragePath;

            storage.Seed(created.Upload!.StoragePath, firstPdf);
            var confirmed = await confirm.HandleAsync(NewConfirmCommand(firstId), Ct);
            confirmed.Outcome.Should().Be(ConfirmPersonalizedDocumentVersionOutcome.Activated);
        }

        // Segunda versión del MISMO tipo: alta + confirm → activa (v2); v1 pasa a histórico.
        Guid secondId;
        await using (var ctx2 = NewContext(dbName))
        {
            var (create, confirm, storage) = Handlers(ctx2);
            var secondPdf = ValidPdf(pages: 2);
            var created = await create.HandleAsync(NewCreateCommand(sha256: ToSha256(secondPdf)), Ct);
            created.Version.Should().Be(2); // AC2 — versión siguiente, nunca reemplaza el número anterior.
            secondId = created.Id!.Value;

            storage.Seed(created.Upload!.StoragePath, secondPdf);
            var confirmed = await confirm.HandleAsync(NewConfirmCommand(secondId), Ct);
            confirmed.Outcome.Should().Be(ConfirmPersonalizedDocumentVersionOutcome.Activated);
        }

        // Verificación en un contexto nuevo: ambas filas existen, ninguna se borró.
        await using var verify = NewContext(dbName);
        var rows = await verify.CompanyPersonalizedDocuments
            .Where(d => d.TenantId == TenantA)
            .ToListAsync(Ct);

        rows.Should().HaveCount(2); // AC2 — ninguna carga borra la anterior.

        var v1 = rows.Single(r => r.Id == firstId);
        v1.Status.Should().Be("historico"); // AC2 — la previa pasa a histórico.
        v1.IsActive.Should().BeFalse();
        v1.StoragePath.Should().Be(firstStoragePath); // AC2 — su archivo queda intacto (nunca se toca storage_path).

        var v2 = rows.Single(r => r.Id == secondId);
        v2.Status.Should().Be("activo");
        v2.IsActive.Should().BeTrue();

        // El listado refleja: activa = v2, historial completo = [v2, v1].
        await using var listCtx = NewContext(dbName);
        var listHandler = new ListPersonalizedDocumentsHandler(new CompanyPersonalizedDocumentRepository(listCtx));
        var groups = await listHandler.HandleAsync(TenantA, Ct);
        var group = groups.Single(g => g.DocumentType == PersonalizedDocumentTypes.Mandato);
        group.Active!.Version.Should().Be(2);
        group.History.Should().HaveCount(2);
    }

    // ---------- AC3: validación de integridad en el confirm (422 + rechazado) ----------

    [Fact]
    public async Task AC3_Confirm_NoMagicBytes_Rejects422_AndStatusRechazado()
    {
        var check = await RunConfirmValidation([0x00, 0x01, 0x02, 0x03, 0x04, 0x05], declaredSha256: null);

        check.Result.Outcome.Should().Be(ConfirmPersonalizedDocumentVersionOutcome.Rejected);
        check.Result.Errors.Should().ContainSingle(e => e.Code == "no_es_pdf");
        check.PersistedStatus.Should().Be("rechazado");
    }

    [Fact]
    public async Task AC3_Confirm_Sha256Mismatch_Rejects422_AndStatusRechazado()
    {
        // El hash declarado en el alta NO coincide con el contenido real subido.
        var check = await RunConfirmValidation(ValidPdf(pages: 1), declaredSha256: new string('0', 64));

        check.Result.Outcome.Should().Be(ConfirmPersonalizedDocumentVersionOutcome.Rejected);
        check.Result.Errors.Should().ContainSingle(e => e.Code == "sha256_mismatch");
        check.PersistedStatus.Should().Be("rechazado");
    }

    [Fact]
    public async Task AC3_Confirm_EncryptedPdf_Rejects422_AndStatusRechazado()
    {
        var check = await RunConfirmValidation(EncryptedPdf());

        check.Result.Outcome.Should().Be(ConfirmPersonalizedDocumentVersionOutcome.Rejected);
        check.Result.Errors.Should().ContainSingle(e => e.Code == "pdf_cifrado");
        check.PersistedStatus.Should().Be("rechazado");
    }

    [Fact]
    public async Task AC3_Confirm_IlegiblePdf_Rejects422_AndStatusRechazado()
    {
        // Magic bytes válidos, resto corrupto: abre y falla al parsear.
        var corrupt = "%PDF-1.7\n"u8.ToArray().Concat(new byte[200]).ToArray();
        var check = await RunConfirmValidation(corrupt);

        check.Result.Outcome.Should().Be(ConfirmPersonalizedDocumentVersionOutcome.Rejected);
        check.Result.Errors.Should().ContainSingle(e => e.Code == "pdf_ilegible");
        check.PersistedStatus.Should().Be("rechazado");
    }

    [Fact]
    public async Task AC3_Confirm_ExceedsMaxSize_Rejects422_AndStatusRechazado()
    {
        var oversized = new byte[PdfIntegrityValidator.MaxSizeBytes + 1];
        "%PDF-"u8.ToArray().CopyTo(oversized, 0);
        var check = await RunConfirmValidation(oversized);

        check.Result.Outcome.Should().Be(ConfirmPersonalizedDocumentVersionOutcome.Rejected);
        check.Result.Errors.Should().ContainSingle(e => e.Code == "excede_tamano");
        check.PersistedStatus.Should().Be("rechazado");
    }

    [Fact]
    public async Task AC3_Confirm_ExceedsMaxPages_Rejects422_AndStatusRechazado()
    {
        var check = await RunConfirmValidation(ValidPdf(pages: PdfIntegrityValidator.MaxPages + 1));

        check.Result.Outcome.Should().Be(ConfirmPersonalizedDocumentVersionOutcome.Rejected);
        check.Result.Errors.Should().ContainSingle(e => e.Code == "excede_paginas");
        check.PersistedStatus.Should().Be("rechazado");
    }

    [Fact]
    public async Task AC3_Confirm_ValidPdf_Activates_AndRecalculatesServerSideHash()
    {
        var bytes = ValidPdf(pages: 3);
        var check = await RunConfirmValidation(bytes);

        check.Result.Outcome.Should().Be(ConfirmPersonalizedDocumentVersionOutcome.Activated);
        check.Result.PageCount.Should().Be(3);
        check.PersistedStatus.Should().Be("activo");
        // El SHA-256 persistido es el RECALCULADO en servidor, no el declarado por el cliente.
        check.Result.Sha256.Should().Be(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));
    }

    // ---------- AC4: interruptor único — canal del tenant ----------

    [Fact]
    public async Task AC4_FlitSmtpChannel_Create_Returns409CanalNoHabilitado()
    {
        var dbName = NewDbName();
        await using var seed = NewContext(dbName);
        SeedFlitSmtpChannel(seed, TenantA);

        await using var ctx = NewContext(dbName);
        var (create, _, storage) = Handlers(ctx);

        var result = await create.HandleAsync(NewCreateCommand(), Ct);

        result.Outcome.Should().Be(CreatePersonalizedDocumentVersionOutcome.ChannelNotEnabled);
        storage.CreateUploadCalls.Should().Be(0);
    }

    [Fact]
    public async Task AC4_FlitSmtpChannel_Confirm_Returns409CanalNoHabilitado()
    {
        var dbName = NewDbName();

        Guid id;
        await using (var seed = NewContext(dbName))
        {
            SeedTenantApiChannel(seed, TenantA);
        }

        await using (var ctx = NewContext(dbName))
        {
            var (create, _, storage) = Handlers(ctx);
            var created = await create.HandleAsync(NewCreateCommand(), Ct);
            id = created.Id!.Value;
            storage.Seed(created.Upload!.StoragePath, ValidPdf(pages: 1));
        }

        // El tenant apaga el interruptor propio DESPUÉS del alta (restricción 9: el historial no se
        // pierde, solo se deshabilitan las rutas de escritura). HU #11362/ADR-0043: ya no se simula
        // apagando el canal — el canal dejó de gobernar esta capacidad.
        await using (var switchChannel = NewContext(dbName))
        {
            var policy = await switchChannel.TenantOperationalPolicies.SingleAsync(p => p.TenantId == TenantA, Ct);
            policy.PersonalizedDocumentsEnabled = false;
            await switchChannel.SaveChangesAsync(Ct);
        }

        await using var confirmCtx = NewContext(dbName);
        var (_, confirmHandler, _) = Handlers(confirmCtx);
        var result = await confirmHandler.HandleAsync(NewConfirmCommand(id), Ct);

        result.Outcome.Should().Be(ConfirmPersonalizedDocumentVersionOutcome.ChannelNotEnabled);
    }

    [Fact]
    public async Task AC4_FlitSmtpChannel_List_StillReturnsHistory()
    {
        // AC4 — el GET no exige el canal: el historial se sigue pudiendo consultar (restricción 9).
        var dbName = NewDbName();

        await using (var seed = NewContext(dbName))
        {
            SeedFlitSmtpChannel(seed, TenantA);
            seed.CompanyPersonalizedDocuments.Add(new CompanyPersonalizedDocumentEntity
            {
                Id = Guid.NewGuid(),
                TenantId = TenantA,
                DocumentType = PersonalizedDocumentTypes.Mandato,
                Version = 1,
                Status = "historico",
                IsActive = false,
                Filename = "mandato.pdf",
                StoragePath = "fm-1",
                StorageSha256 = new string('a', 64),
                SizeBytes = 10,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(Ct);
        }

        await using var ctx = NewContext(dbName);
        var listHandler = new ListPersonalizedDocumentsHandler(new CompanyPersonalizedDocumentRepository(ctx));
        var groups = await listHandler.HandleAsync(TenantA, Ct);

        groups.Should().ContainSingle(g => g.DocumentType == PersonalizedDocumentTypes.Mandato);
    }

    // ---------- AC5: negativo de aislamiento cross-tenant ----------

    [Fact]
    public async Task AC5_Confirm_ForeignTenantId_ReturnsNotFound_NeverActivatesForeignRow()
    {
        var dbName = NewDbName();

        await using (var seed = NewContext(dbName))
        {
            SeedTenantApiChannel(seed, TenantA);
            SeedTenantApiChannel(seed, TenantB);
        }

        Guid idOfA;
        string storagePathOfA;
        await using (var ctxA = NewContext(dbName))
        {
            var (create, _, storage) = Handlers(ctxA);
            var created = await create.HandleAsync(NewCreateCommand(tenantId: TenantA), Ct);
            idOfA = created.Id!.Value;
            storagePathOfA = created.Upload!.StoragePath;
            storage.Seed(storagePathOfA, ValidPdf(pages: 1));
        }

        // Administrador de la compañía B intenta confirmar el id de la compañía A: WHERE tenant_id
        // explícito del repositorio debe devolver "no existe", no la fila de A (RLS es decorativo).
        await using var ctxB = NewContext(dbName);
        var (_, confirmAsB, storageB) = Handlers(ctxB);
        var result = await confirmAsB.HandleAsync(NewConfirmCommand(idOfA, tenantId: TenantB), Ct);

        result.Outcome.Should().Be(ConfirmPersonalizedDocumentVersionOutcome.NotFound);
        storageB.OpenReadCalls.Should().Be(0); // nunca se emite/abre el artefacto de A para B.

        // La fila de A sigue intacta y en pendiente (el intento de B no la tocó).
        await using var verify = NewContext(dbName);
        var rowOfA = await verify.CompanyPersonalizedDocuments.SingleAsync(d => d.Id == idOfA, Ct);
        rowOfA.TenantId.Should().Be(TenantA);
        rowOfA.Status.Should().Be("pendiente");
    }

    [Fact]
    public async Task AC5_List_OnlyReturnsRowsOfOwnTenant()
    {
        var dbName = NewDbName();

        await using (var seed = NewContext(dbName))
        {
            SeedTenantApiChannel(seed, TenantA);
            SeedTenantApiChannel(seed, TenantB);
        }

        await using (var ctxA = NewContext(dbName))
        {
            var (create, _, storage) = Handlers(ctxA);
            var created = await create.HandleAsync(NewCreateCommand(tenantId: TenantA), Ct);
            storage.Seed(created.Upload!.StoragePath, ValidPdf(pages: 1));
        }

        await using (var ctxB = NewContext(dbName))
        {
            var (create, _, storage) = Handlers(ctxB);
            var created = await create.HandleAsync(NewCreateCommand(tenantId: TenantB, documentType: PersonalizedDocumentTypes.TramiteVirtual), Ct);
            storage.Seed(created.Upload!.StoragePath, ValidPdf(pages: 1));
        }

        await using var listCtx = NewContext(dbName);
        var listHandler = new ListPersonalizedDocumentsHandler(new CompanyPersonalizedDocumentRepository(listCtx));

        var groupsOfA = await listHandler.HandleAsync(TenantA, Ct);
        groupsOfA.Should().ContainSingle(g => g.DocumentType == PersonalizedDocumentTypes.Mandato);
        groupsOfA.Should().NotContain(g => g.DocumentType == PersonalizedDocumentTypes.TramiteVirtual);

        var groupsOfB = await listHandler.HandleAsync(TenantB, Ct);
        groupsOfB.Should().ContainSingle(g => g.DocumentType == PersonalizedDocumentTypes.TramiteVirtual);
        groupsOfB.Should().NotContain(g => g.DocumentType == PersonalizedDocumentTypes.Mandato);
    }

    // ---------- Helpers ----------

    private async Task<(ConfirmPersonalizedDocumentVersionResult Result, string PersistedStatus)> RunConfirmValidation(
        byte[] uploadedBytes,
        string? declaredSha256 = null)
    {
        var dbName = NewDbName();

        await using (var seed = NewContext(dbName))
        {
            SeedTenantApiChannel(seed, TenantA);
        }

        Guid id;
        await using (var ctx1 = NewContext(dbName))
        {
            var (create, _, storage) = Handlers(ctx1);
            var created = await create.HandleAsync(
                NewCreateCommand(sha256: declaredSha256 ?? ToSha256(uploadedBytes)), Ct);
            id = created.Id!.Value;
            storage.Seed(created.Upload!.StoragePath, uploadedBytes);
        }

        ConfirmPersonalizedDocumentVersionResult result;
        await using (var ctx2 = NewContext(dbName))
        {
            var (_, confirm, storage) = Handlers(ctx2);
            // Re-sembrar en el storage del segundo contexto (el fake es por instancia de handler set).
            var row = await ctx2.CompanyPersonalizedDocuments.SingleAsync(d => d.TenantId == TenantA, Ct);
            storage.Seed(row.StoragePath, uploadedBytes);
            result = await confirm.HandleAsync(NewConfirmCommand(id), Ct);
        }

        await using var verify = NewContext(dbName);
        var persisted = await verify.CompanyPersonalizedDocuments.SingleAsync(d => d.Id == id, Ct);
        return (result, persisted.Status);
    }

    private static string ToSha256(byte[] bytes) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));

    private static CreatePersonalizedDocumentVersionCommand NewCreateCommand(
        Guid? tenantId = null,
        string documentType = PersonalizedDocumentTypes.Mandato,
        string filename = "mandato.pdf",
        string? sha256 = "deadbeef",
        long sizeBytes = 1024) =>
        new()
        {
            TenantId = tenantId ?? TenantA,
            DocumentType = documentType,
            Filename = filename,
            Sha256 = sha256,
            SizeBytes = sizeBytes,
            CreatedBy = ActorId,
        };

    private static ConfirmPersonalizedDocumentVersionCommand NewConfirmCommand(Guid id, Guid? tenantId = null) =>
        new()
        {
            TenantId = tenantId ?? TenantA,
            Id = id,
            ConfirmedBy = ActorId,
        };

    private static (
        CreatePersonalizedDocumentVersionHandler Create,
        ConfirmPersonalizedDocumentVersionHandler Confirm,
        FakePersonalizedDocumentStorage Storage) Handlers(FlitDbContext ctx)
    {
        var repository = new CompanyPersonalizedDocumentRepository(ctx);
        var settingsRepository = new TenantSettingsRepository(ctx, NullAuditContextAccessor.Instance);
        var storage = new FakePersonalizedDocumentStorage();
        var inspector = new PdfSharpDocumentInspector(NullLogger<PdfSharpDocumentInspector>.Instance);
        var validator = new PdfIntegrityValidator(inspector);
        var auditWriter = Substitute.For<IAdminAuditWriter>();

        return (
            new CreatePersonalizedDocumentVersionHandler(storage, repository, settingsRepository, auditWriter, NullAuditContextAccessor.Instance),
            new ConfirmPersonalizedDocumentVersionHandler(storage, repository, settingsRepository, validator, auditWriter, NullAuditContextAccessor.Instance),
            storage);
    }

    private static string NewDbName() => $"flit-personalized-docs-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);

    private static string TenantApiChannel(Guid tenantId)
    {
        // Devuelve el nombre de la BD ya sembrada (conveniencia para tests de un solo contexto).
        var dbName = NewDbName();
        using var ctx = NewContext(dbName);
        SeedTenantApiChannel(ctx, tenantId);
        return dbName;
    }

    private static void SeedTenantApiChannel(FlitDbContext ctx, Guid tenantId) =>
        SeedPolicy(ctx, tenantId, "tenant_api");

    private static void SeedFlitSmtpChannel(FlitDbContext ctx, Guid tenantId) =>
        SeedPolicy(ctx, tenantId, "flit_smtp");

    private static void SeedPolicy(FlitDbContext ctx, Guid tenantId, string channel)
    {
        ctx.TenantOperationalPolicies.Add(new TenantOperationalPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            NotificationChannel = channel,
            // HU #11357/#11362 (ADR-0043) — el guard ya no lee el canal: se sigue derivando aquí de
            // "tenant_api" solo para conservar el fixture existente sin reescribir cada test AC1-AC5
            // de esta clase (que preceden a la HU #11362). La cobertura de que el canal YA NO gobierna
            // esta capacidad vive en PersonalizedDocumentEligibilityGuardTests / TenantChannelEmailRouterTests.
            PersonalizedDocumentsEnabled = string.Equals(channel, "tenant_api", StringComparison.Ordinal),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
    }

    /// <summary>PDF válido con <paramref name="pages"/> páginas, generado con PdfSharpCore.</summary>
    private static byte[] ValidPdf(int pages)
    {
        using var document = new PdfDocument();
        for (var i = 0; i < pages; i++)
        {
            document.AddPage();
        }

        using var stream = new MemoryStream();
        document.Save(stream, closeStream: false);
        return stream.ToArray();
    }

    /// <summary>PDF cifrado (owner password) — se abre sin pedir contraseña, pero queda marcado cifrado.</summary>
    private static byte[] EncryptedPdf()
    {
        using var document = new PdfDocument();
        document.AddPage();
        document.SecuritySettings.OwnerPassword = "owner-secret";

        using var stream = new MemoryStream();
        document.Save(stream, closeStream: false);
        return stream.ToArray();
    }

    /// <summary>Storage en memoria: no toca red; guarda bytes por <c>storagePath</c> para el confirm.</summary>
    private sealed class FakePersonalizedDocumentStorage : ICompanyPersonalizedDocumentStorage
    {
        private readonly Dictionary<string, byte[]> _objects = [];
        private int _sequence;

        public int CreateUploadCalls { get; private set; }
        public int OpenReadCalls { get; private set; }

        public void Seed(string storagePath, byte[] bytes) => _objects[storagePath] = bytes;

        public Task<PersonalizedDocumentUploadTicket> CreateUploadAsync(
            Guid tenantId, string documentType, CancellationToken cancellationToken = default)
        {
            CreateUploadCalls++;
            var storagePath = $"fm-personalized-{tenantId}-{documentType}-{++_sequence}";
            return Task.FromResult(new PersonalizedDocumentUploadTicket(
                storagePath,
                "https://s3.example/upload",
                new Dictionary<string, string> { ["key"] = storagePath }));
        }

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
        {
            OpenReadCalls++;
            if (!_objects.TryGetValue(storagePath, out var bytes))
            {
                return Task.FromResult<Stream?>(null);
            }

            return Task.FromResult<Stream?>(new MemoryStream(bytes, writable: false));
        }

        public int GetViewUrlCalls { get; private set; }

        public Task<PersonalizedDocumentView?> GetViewUrlAsync(string storagePath, CancellationToken cancellationToken = default)
        {
            GetViewUrlCalls++;
            if (!_objects.ContainsKey(storagePath))
            {
                return Task.FromResult<PersonalizedDocumentView?>(null);
            }

            return Task.FromResult<PersonalizedDocumentView?>(
                new PersonalizedDocumentView($"https://s3.example/view/{storagePath}", DateTimeOffset.UtcNow.AddMinutes(10)));
        }
    }
}
