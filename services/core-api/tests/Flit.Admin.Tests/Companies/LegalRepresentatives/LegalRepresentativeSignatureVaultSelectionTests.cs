using Flit.Admin.Application.Companies.LegalRepresentatives;
using Flit.Admin.Application.Companies.LegalRepresentatives.CreateLegalRepresentative;
using Flit.Admin.Application.Companies.LegalRepresentatives.GetLegalRepresentative;
using Flit.Admin.Application.Companies.SignatureVault;
using Flit.Admin.Application.Companies.SignatureVault.CreateSignatureVault;
using Flit.Admin.Application.Companies.SignatureVault.ListSignatureVault;
using Flit.Admin.Domain.Companies.SignatureVault;
using Flit.Admin.Domain.DocumentRequirements;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using SignatureVaultAggregate = Flit.Admin.Domain.Companies.SignatureVault.SignatureVault;

namespace Flit.Admin.Tests.Companies.LegalRepresentatives;

/// <summary>
/// Tests de selección explícita de firma del baúl al guardar un representante (HU #11175, AC1–AC5).
/// Cubren:
/// AC1 — listado del baúl filtrando por (documentType + documentNumber): solo firmas de esa persona.
/// AC2 — listado con soloVigentes = true: se excluyen las vencidas.
/// AC3 — guardar el representante con SignatureVaultId explícito: se persiste la firma elegida.
/// AC4 — firma de otra persona, vencida, o inexistente → 422 con código estructurado.
/// AC5 — guardar sin SignatureVaultId: comportamiento anterior intacto (resolutor automático).
/// </summary>
public sealed class LegalRepresentativeSignatureVaultSelectionTests
{
    private static readonly Guid Tenant = Guid.Parse("77777777-0000-4000-8000-00000000ee01");
    private const string Nit = "900000000-9";
    private const string DocType = "CC";
    private const string DocNumber = "987654321";
    private const string OtherDocNumber = "111222333";
    private const string PngBase64 = "iVBORw0KGgoAAAANSUhEUg==";

    // "Hoy" fijo en Colombia (UTC-5): 2026-07-31.
    private static readonly DateOnly Today = new(2026, 7, 31);
    private static readonly DateTimeOffset NowCo = new(Today.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(-5));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ─── AC1 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_ListSignatureVault_FilterByDocument_ReturnsOnlyMatchingPerson()
    {
        await using var ctx = NewContext();
        var vaultCreate = VaultCreateHandler(ctx);

        // Sembrar dos firmas: una para DocNumber, otra para OtherDocNumber.
        await vaultCreate.HandleAsync(NewVaultCreate(DocNumber), Ct);
        await vaultCreate.HandleAsync(NewVaultCreate(OtherDocNumber), Ct);

        var handler = new ListSignatureVaultHandler(new DbSignatureVaultReader(ctx), new StubTimeProvider(NowCo));

        var result = await handler.HandleAsync(new ListSignatureVaultQuery
        {
            TenantId = Tenant,
            DocumentType = DocType,
            DocumentNumber = DocNumber,
        }, Ct);

        result.Should().ContainSingle("solo debe devolver la firma del documento indicado");
        result[0].DocumentNumber.Should().Be(DocNumber);
        result[0].DocumentType.Should().Be(DocType);
    }

    // ─── AC2 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC2_ListSignatureVault_SoloVigentes_ExcludesExpired()
    {
        await using var ctx = NewContext();
        var vaultCreate = VaultCreateHandler(ctx);

        // Sembrar dos firmas del mismo documento: una vigente (2026), otra vencida (2025).
        await vaultCreate.HandleAsync(NewVaultCreate(DocNumber,
            desde: new DateOnly(2026, 1, 1), hasta: new DateOnly(2026, 12, 31)), Ct);
        await vaultCreate.HandleAsync(NewVaultCreate(DocNumber,
            desde: new DateOnly(2025, 1, 1), hasta: new DateOnly(2025, 6, 30)), Ct);

        var handler = new ListSignatureVaultHandler(new DbSignatureVaultReader(ctx), new StubTimeProvider(NowCo));

        var result = await handler.HandleAsync(new ListSignatureVaultQuery
        {
            TenantId = Tenant,
            DocumentType = DocType,
            DocumentNumber = DocNumber,
            SoloVigentes = true,
        }, Ct);

        result.Should().ContainSingle("soloVigentes=true debe excluir la firma vencida");
        result[0].VigenciaHasta.Should().BeOnOrAfter(Today);
    }

    [Fact]
    public async Task AC2_ListSignatureVault_SinFiltro_ReturnsBothVigenteAndExpired()
    {
        await using var ctx = NewContext();
        var vaultCreate = VaultCreateHandler(ctx);

        await vaultCreate.HandleAsync(NewVaultCreate(DocNumber,
            desde: new DateOnly(2026, 1, 1), hasta: new DateOnly(2026, 12, 31)), Ct);
        await vaultCreate.HandleAsync(NewVaultCreate(DocNumber,
            desde: new DateOnly(2025, 1, 1), hasta: new DateOnly(2025, 6, 30)), Ct);

        var handler = new ListSignatureVaultHandler(new DbSignatureVaultReader(ctx), new StubTimeProvider(NowCo));

        // Sin soloVigentes → ambas firmas deben aparecer.
        var result = await handler.HandleAsync(new ListSignatureVaultQuery
        {
            TenantId = Tenant,
            DocumentType = DocType,
            DocumentNumber = DocNumber,
        }, Ct);

        result.Should().HaveCount(2, "sin soloVigentes se devuelven todas las firmas del documento");
    }

    // ─── AC3 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC3_SaveRepresentative_WithExplicitSignatureVaultId_PersistsChosenSignature()
    {
        await using var ctx = NewContext();

        // Sembrar una firma vigente del representante en el baúl.
        var createResult = await VaultCreateHandler(ctx).HandleAsync(
            NewVaultCreate(DocNumber), Ct);
        createResult.IsValid.Should().BeTrue();
        var firmaId = createResult.SignatureVaultId!.Value;

        var vaultReader = new DbSignatureVaultReader(ctx);
        var h = RepHandlers(ctx, vaultReader, resolverResolution: LegalRepresentativeSignatureResolution.None);

        var result = await h.Create.HandleAsync(NewRepCreate(signatureVaultId: firmaId), Ct);

        result.IsValid.Should().BeTrue("la firma elegida es válida (mismo documento, activa y vigente)");
        result.Signals.Should().NotContain(LegalRepresentativeSignals.SinFirmaNiIdentidad);

        var item = await h.Get.HandleAsync(
            new GetLegalRepresentativeByIdQuery { TenantId = Tenant, Id = result.Id!.Value }, Ct);
        item.Should().NotBeNull();
        item!.SignatureVaultId.Should().Be(firmaId,
            "la firma elegida explícitamente debe persistirse, no la del resolutor");
    }

    // ─── AC4 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC4_SaveRepresentative_WithOtherPersonSignature_Returns422_DocumentoNoCoincide()
    {
        await using var ctx = NewContext();

        // Firma de OTRA persona (OtherDocNumber).
        var createResult = await VaultCreateHandler(ctx).HandleAsync(
            NewVaultCreate(OtherDocNumber), Ct);
        var firmaOtraId = createResult.SignatureVaultId!.Value;

        var h = RepHandlers(ctx, new DbSignatureVaultReader(ctx),
            resolverResolution: LegalRepresentativeSignatureResolution.None);

        // El representante tiene DocNumber, la firma pertenece a OtherDocNumber.
        var result = await h.Create.HandleAsync(NewRepCreate(signatureVaultId: firmaOtraId), Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.Field == "signatureVaultId" && e.Code == "firma_documento_no_coincide",
            "la firma no pertenece al representante indicado");
    }

    [Fact]
    public async Task AC4_SaveRepresentative_WithExpiredSignature_Returns422_FirmaNoVigente()
    {
        await using var ctx = NewContext();

        // Firma del mismo documento pero VENCIDA (vigencia hasta 2025, hoy es 2026-07-31).
        var createResult = await VaultCreateHandler(ctx).HandleAsync(
            NewVaultCreate(DocNumber, desde: new DateOnly(2025, 1, 1), hasta: new DateOnly(2025, 6, 30)), Ct);
        var firmaVencidaId = createResult.SignatureVaultId!.Value;

        var h = RepHandlers(ctx, new DbSignatureVaultReader(ctx),
            resolverResolution: LegalRepresentativeSignatureResolution.None);

        var result = await h.Create.HandleAsync(NewRepCreate(signatureVaultId: firmaVencidaId), Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.Field == "signatureVaultId" && e.Code == "firma_no_vigente",
            "la firma está vencida en la fecha actual");
    }

    [Fact]
    public async Task AC4_SaveRepresentative_WithNonExistentSignatureId_Returns422_FirmaNoEncontrada()
    {
        await using var ctx = NewContext();

        var h = RepHandlers(ctx, new DbSignatureVaultReader(ctx),
            resolverResolution: LegalRepresentativeSignatureResolution.None);

        // GUID que no existe en el baúl del tenant.
        var result = await h.Create.HandleAsync(NewRepCreate(signatureVaultId: Guid.NewGuid()), Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.Field == "signatureVaultId" && e.Code == "firma_no_encontrada",
            "la firma no existe en el baúl de este tenant");
    }

    // ─── AC5 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC5_SaveRepresentative_WithoutSignatureVaultId_UsesAutomaticResolver()
    {
        await using var ctx = NewContext();

        var resolverSignatureId = Guid.NewGuid();
        // El resolutor devuelve una firma específica; NO se llama a GetByIdAsync del baúl.
        var h = RepHandlers(ctx, new NullSignatureVaultReader(),
            resolverResolution: LegalRepresentativeSignatureResolution.FromSignature(resolverSignatureId));

        var result = await h.Create.HandleAsync(NewRepCreate(signatureVaultId: null), Ct);

        result.IsValid.Should().BeTrue();
        var item = await h.Get.HandleAsync(
            new GetLegalRepresentativeByIdQuery { TenantId = Tenant, Id = result.Id!.Value }, Ct);
        item!.SignatureVaultId.Should().Be(resolverSignatureId,
            "sin SignatureVaultId el resolutor automático elige la firma (AC5)");
    }

    [Fact]
    public async Task AC5_SaveRepresentative_WithoutSignatureVaultId_NoSignature_EmitsSinFirmaNiIdentidad()
    {
        await using var ctx = NewContext();

        var h = RepHandlers(ctx, new NullSignatureVaultReader(),
            resolverResolution: LegalRepresentativeSignatureResolution.None);

        var result = await h.Create.HandleAsync(NewRepCreate(signatureVaultId: null), Ct);

        result.IsValid.Should().BeTrue("guardar sin firma ni identidad es válido: se emite señal");
        result.Signals.Should().ContainSingle()
            .Which.Should().Be(LegalRepresentativeSignals.SinFirmaNiIdentidad,
                "sin firma ni identidad disponible se emite la señal correspondiente (AC5)");
    }

    // ─── Factories ─────────────────────────────────────────────────────────────

    private static CreateSignatureVaultCommand NewVaultCreate(
        string documentNumber,
        DateOnly? desde = null,
        DateOnly? hasta = null) =>
        new()
        {
            TenantId = Tenant,
            DocumentType = DocType,
            DocumentNumber = documentNumber,
            NitEmpresa = Nit,
            FullName = $"Titular {documentNumber}",
            VigenciaDesde = desde ?? new DateOnly(2026, 1, 1),
            VigenciaHasta = hasta ?? new DateOnly(2026, 12, 31),
            ArtefactoFirmaBase64 = PngBase64,
        };

    private static CreateLegalRepresentativeCommand NewRepCreate(Guid? signatureVaultId) =>
        new()
        {
            TenantId = Tenant,
            CompanyNit = Nit,
            CompanyName = "Empresa de Prueba S.A.S.",
            DocumentType = DocType,
            DocumentNumber = DocNumber,
            FirstLastName = "Representante",
            Name = "Juan",
            ProcedureTypeIds = [],
            SignatureVaultId = signatureVaultId,
        };

    private static CreateSignatureVaultHandler VaultCreateHandler(FlitDbContext ctx)
    {
        var storage = new FakeArtifactStorage();
        var repo = new SignatureVaultRepository(ctx);
        return new CreateSignatureVaultHandler(storage, repo);
    }

    private static (CreateLegalRepresentativeHandler Create, GetLegalRepresentativeByIdHandler Get)
        RepHandlers(
            FlitDbContext ctx,
            ISignatureVaultReader signatureVaultReader,
            LegalRepresentativeSignatureResolution resolverResolution)
    {
        var reader = new DbLegalRepresentativeReader(ctx);
        var repo = new LegalRepresentativeRepository(ctx);
        var catalog = new EmptyProcedureTypeCatalog();
        var resolver = new FixedSignatureResolver(resolverResolution);
        var clock = new StubTimeProvider(NowCo);
        var writer = new LegalRepresentativeWriter(catalog, resolver, signatureVaultReader, repo, reader, clock);
        return (new CreateLegalRepresentativeHandler(writer), new GetLegalRepresentativeByIdHandler(reader));
    }

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-sv-selection-{Guid.NewGuid()}")
            .Options);

    // ─── Fakes ─────────────────────────────────────────────────────────────────

    private sealed class FakeArtifactStorage : ISignatureVaultArtifactStorage
    {
        public Task<StoredSignatureArtifact> SaveAsync(
            Guid tenantId, byte[] artifact, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StoredSignatureArtifact("fm-file-test", "sha256-test"));
    }

    private sealed class EmptyProcedureTypeCatalog : IProcedureTypeCatalog
    {
        public Task<bool> ExistsAsync(Guid procedureTypeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<ProcedureTypeCatalogItem>> ListActivePublishedAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProcedureTypeCatalogItem>>([]);
    }

    private sealed class FixedSignatureResolver : ILegalRepresentativeSignatureResolver
    {
        private readonly LegalRepresentativeSignatureResolution _resolution;

        public FixedSignatureResolver(LegalRepresentativeSignatureResolution resolution) =>
            _resolution = resolution;

        public Task<LegalRepresentativeSignatureResolution> ResolveAsync(
            Guid tenantId, string nitCompania, string tipoDocumento, string documentoRepresentante,
            DateOnly today, CancellationToken cancellationToken = default) =>
            Task.FromResult(_resolution);
    }

    private sealed class NullSignatureVaultReader : ISignatureVaultReader
    {
        public Task<SignatureVaultAggregate?> FindActiveByNitAsync(
            Guid tenantId, string nitEmpresa, CancellationToken cancellationToken = default) =>
            Task.FromResult<SignatureVaultAggregate?>(null);

        public Task<SignatureVaultAggregate?> FindActiveByDocumentAsync(
            Guid tenantId, string documentType, string documentNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult<SignatureVaultAggregate?>(null);

        public Task<IReadOnlyList<SignatureVaultItem>> ListByTenantAsync(
            Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SignatureVaultItem>>([]);

        public Task<SignatureVaultItem?> GetByIdAsync(
            Guid tenantId, Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<SignatureVaultItem?>(null);
    }

    private sealed class StubTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public StubTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
