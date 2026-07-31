using Flit.Admin.Application.Companies.LegalRepresentatives;
using Flit.Admin.Application.Companies.LegalRepresentatives.CreateLegalRepresentative;
using Flit.Admin.Domain.Companies.LegalRepresentatives;
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
/// Tests del historial de escrituras del representante vía sus empresas (HU #10933) sobre
/// <see cref="DbLegalRepresentativeReader"/> + <see cref="DeedRepository"/> (InMemory). Cubren:
/// el detalle agrupa las escrituras por CADA compañía del representante; el historial completo
/// (vigentes, vencidas, futuras e inactivas) aparece con su <c>Estado</c> anclado a "hoy" en
/// Colombia; el orden es por <c>VigenciaHasta</c> descendente; una escritura compartida por varias
/// compañías se lista en cada una; y el listado paginado NO carga escrituras (contrato ligero).
/// </summary>
public sealed class LegalRepresentativeDeedsHistoryTests
{
    private static readonly Guid Tenant = Guid.Parse("99999999-0000-4000-8000-0000000000aa");
    private const string DocRep = "123456789";
    private const string NitA = "900000000-1";
    private const string NitB = "800000000-2";

    // "Hoy" fijo en Colombia para que el Estado sea determinista.
    private static readonly DateOnly Today = new(2026, 7, 23);
    private static readonly TimeProvider Clock =
        new StubTimeProvider(new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.FromHours(-5)));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetById_ReturnsFullHistory_PerCompany_WithEstado_OrderedByVigenciaHastaDesc()
    {
        await using var ctx = NewContext();
        var repId = await SeedRepAsync(ctx, [NitA]);
        var companyA = await CompanyIdAsync(ctx, repId, NitA);

        // Historial de la compañía A: vigente, vencida y futura.
        var vigente = await SeedDeedAsync(ctx, "Poder vigente", new(2026, 1, 1), new(2026, 12, 31), [companyA], repId);
        var vencida = await SeedDeedAsync(ctx, "Poder vencido", new(2025, 1, 1), new(2025, 12, 31), [companyA], repId);
        var futura = await SeedDeedAsync(ctx, "Poder futuro", new(2027, 1, 1), new(2027, 12, 31), [companyA], repId);

        var reader = new DbLegalRepresentativeReader(ctx, Clock);
        var item = await reader.GetByIdAsync(Tenant, repId, Ct);

        item.Should().NotBeNull();
        var company = item!.Companies.Should().ContainSingle().Subject;
        company.Nit.Should().Be(NitA);
        company.Deeds.Should().HaveCount(3);

        // Orden por VigenciaHasta descendente: futura (2027) → vigente (2026) → vencida (2025).
        company.Deeds.Select(d => d.Id).Should().ContainInOrder(futura, vigente, vencida);

        company.Deeds.Single(d => d.Id == vigente).Estado.Should().Be(RepresentativeDeedSummary.EstadoVigente);
        company.Deeds.Single(d => d.Id == vencida).Estado.Should().Be(RepresentativeDeedSummary.EstadoVencida);
        company.Deeds.Single(d => d.Id == futura).Estado.Should().Be(RepresentativeDeedSummary.EstadoFutura);
    }

    [Fact]
    public async Task GetById_GroupsDeeds_ByEachCompanyOfTheRepresentative()
    {
        await using var ctx = NewContext();
        var repId = await SeedRepAsync(ctx, [NitA, NitB]);
        var companyA = await CompanyIdAsync(ctx, repId, NitA);
        var companyB = await CompanyIdAsync(ctx, repId, NitB);

        var deedA = await SeedDeedAsync(ctx, "Escritura A", new(2026, 1, 1), new(2026, 12, 31), [companyA], repId);
        var deedB = await SeedDeedAsync(ctx, "Escritura B", new(2026, 1, 1), new(2026, 12, 31), [companyB], repId);

        var reader = new DbLegalRepresentativeReader(ctx, Clock);
        var item = await reader.GetByIdAsync(Tenant, repId, Ct);

        item!.Companies.Should().HaveCount(2);
        var a = item.Companies.Single(c => c.Nit == NitA);
        var b = item.Companies.Single(c => c.Nit == NitB);

        a.Deeds.Should().ContainSingle().Which.Id.Should().Be(deedA);
        b.Deeds.Should().ContainSingle().Which.Id.Should().Be(deedB);
    }

    [Fact]
    public async Task GetById_SharedDeed_AppearsInEachCompany()
    {
        await using var ctx = NewContext();
        var repId = await SeedRepAsync(ctx, [NitA, NitB]);
        var companyA = await CompanyIdAsync(ctx, repId, NitA);
        var companyB = await CompanyIdAsync(ctx, repId, NitB);

        // Una sola escritura que cubre A y B (puente M:N).
        var shared = await SeedDeedAsync(ctx, "Poder conjunto", new(2026, 1, 1), new(2026, 12, 31), [companyA, companyB], repId);

        var reader = new DbLegalRepresentativeReader(ctx, Clock);
        var item = await reader.GetByIdAsync(Tenant, repId, Ct);

        var a = item!.Companies.Single(c => c.Nit == NitA);
        var b = item.Companies.Single(c => c.Nit == NitB);
        a.Deeds.Should().ContainSingle().Which.Id.Should().Be(shared);
        b.Deeds.Should().ContainSingle().Which.Id.Should().Be(shared);
    }

    [Fact]
    public async Task GetById_InactiveDeed_HasEstadoInactiva_EvenIfWithinVigencia()
    {
        await using var ctx = NewContext();
        var repId = await SeedRepAsync(ctx, [NitA]);
        var companyA = await CompanyIdAsync(ctx, repId, NitA);

        // Vigencia que contiene "hoy" pero dada de baja → prevalece "inactiva".
        var deedId = await SeedDeedAsync(ctx, "Poder revocado", new(2026, 1, 1), new(2026, 12, 31), [companyA], repId);
        await new DeedRepository(ctx).DeactivateAsync(Tenant, deedId, changedBy: null, Ct);

        var reader = new DbLegalRepresentativeReader(ctx, Clock);
        var item = await reader.GetByIdAsync(Tenant, repId, Ct);

        var deed = item!.Companies.Single().Deeds.Should().ContainSingle().Subject;
        deed.IsActive.Should().BeFalse();
        deed.Estado.Should().Be(RepresentativeDeedSummary.EstadoInactiva);
    }

    [Fact]
    public async Task GetById_TwoRepsSharingCompany_EachSeesOnlyOwnDeed_LegacyExcluded()
    {
        // Feature #10929 — dos representantes que COMPARTEN la empresa A, cada uno con su propia
        // escritura. El detalle de cada uno debe mostrar SOLO la suya; una escritura legada
        // (representative_id null) NO aparece en el detalle de ninguno.
        await using var ctx = NewContext();
        var repA = await SeedRepAsync(ctx, [NitA], doc: "111");
        var repB = await SeedRepAsync(ctx, [NitA], doc: "222");
        var companyA = await CompanyIdAsync(ctx, repA, NitA); // misma compañía (upsert por NIT).

        var deedA = await SeedDeedAsync(ctx, "Escritura de A", new(2026, 1, 1), new(2026, 12, 31), [companyA], repA);
        var deedB = await SeedDeedAsync(ctx, "Escritura de B", new(2026, 1, 1), new(2026, 12, 31), [companyA], repB);
        await SeedDeedAsync(ctx, "Escritura legada", new(2026, 1, 1), new(2026, 12, 31), [companyA], representativeId: null);

        var reader = new DbLegalRepresentativeReader(ctx, Clock);
        var detailA = await reader.GetByIdAsync(Tenant, repA, Ct);
        var detailB = await reader.GetByIdAsync(Tenant, repB, Ct);

        // A ve solo la suya; B ve solo la suya. La legada no aparece en ninguno.
        detailA!.Companies.Single(c => c.Nit == NitA).Deeds
            .Should().ContainSingle().Which.Id.Should().Be(deedA);
        detailB!.Companies.Single(c => c.Nit == NitA).Deeds
            .Should().ContainSingle().Which.Id.Should().Be(deedB);
    }

    [Fact]
    public async Task ListPaged_DoesNotLoadDeeds_KeepsLightweightContract()
    {
        await using var ctx = NewContext();
        var repId = await SeedRepAsync(ctx, [NitA]);
        var companyA = await CompanyIdAsync(ctx, repId, NitA);
        await SeedDeedAsync(ctx, "Escritura", new(2026, 1, 1), new(2026, 12, 31), [companyA], repId);

        var reader = new DbLegalRepresentativeReader(ctx, Clock);
        var page = await reader.ListPagedAsync(Tenant, 1, 50, Ct);

        var rep = page.Items.Should().ContainSingle().Subject;
        rep.Companies.Should().ContainSingle();
        rep.Companies.Single().Deeds.Should().BeEmpty();
    }

    // ---------- Seeding helpers ----------

    /// <summary>Crea el representante (y sus compañías) vía el handler real; devuelve su id.</summary>
    private static async Task<Guid> SeedRepAsync(FlitDbContext ctx, IReadOnlyList<string> nits, string? doc = null)
    {
        var reader = new DbLegalRepresentativeReader(ctx, Clock);
        var repo = new LegalRepresentativeRepository(ctx);
        var writer = new LegalRepresentativeWriter(
            new FakeProcedureTypeCatalog(),
            new FakeSignatureResolver(),
            new FakeSignatureVaultReader(),
            repo, reader, Clock);
        var create = new CreateLegalRepresentativeHandler(writer);

        var result = await create.HandleAsync(new CreateLegalRepresentativeCommand
        {
            TenantId = Tenant,
            DocumentType = "CC",
            DocumentNumber = doc ?? DocRep,
            FirstLastName = "Perez",
            Name = "Juan Perez",
            Companies = [.. nits.Select(n => new LegalRepresentativeCompanyInput(n, $"Empresa {n}", null, null, null, null))],
        }, Ct);

        result.IsValid.Should().BeTrue();
        return result.Id!.Value;
    }

    /// <summary>Resuelve el id de la compañía representada del rep por su NIT.</summary>
    private static async Task<Guid> CompanyIdAsync(FlitDbContext ctx, Guid repId, string nit)
    {
        var item = await new DbLegalRepresentativeReader(ctx, Clock).GetByIdAsync(Tenant, repId, Ct);
        return item!.Companies.Single(c => c.Nit == nit).Id;
    }

    /// <summary>
    /// Crea una escritura activa vinculada a las compañías dadas y al representante
    /// <paramref name="representativeId"/> (Feature #10929); devuelve su id. <c>null</c> = escritura
    /// legada (sin representante), que NO debe aparecer en el detalle de ningún representante.
    /// </summary>
    private static Task<Guid> SeedDeedAsync(
        FlitDbContext ctx,
        string description,
        DateOnly desde,
        DateOnly hasta,
        IReadOnlyList<Guid> companyIds,
        Guid? representativeId) =>
        new DeedRepository(ctx).CreateAsync(
            new SaveDeedData(
                TenantId: Tenant,
                Id: null,
                Description: description,
                StoragePath: "fm-deed",
                StorageSha256: "deadbeef",
                VigenciaDesde: desde,
                VigenciaHasta: hasta,
                RepresentedCompanyIds: companyIds,
                ActorBy: null,
                RepresentativeId: representativeId),
            Ct);

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-rep-deeds-{Guid.NewGuid()}")
            .Options);

    private sealed class FakeProcedureTypeCatalog : IProcedureTypeCatalog
    {
        public Task<bool> ExistsAsync(Guid procedureTypeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<IReadOnlyList<ProcedureTypeCatalogItem>> ListActivePublishedAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProcedureTypeCatalogItem>>([]);
    }

    private sealed class FakeSignatureResolver : ILegalRepresentativeSignatureResolver
    {
        public Task<LegalRepresentativeSignatureResolution> ResolveAsync(
            Guid tenantId,
            string nitCompania,
            string tipoDocumento,
            string documentoRepresentante,
            DateOnly today,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(LegalRepresentativeSignatureResolution.None);
    }

    private sealed class FakeSignatureVaultReader : ISignatureVaultReader
    {
        public Task<SignatureVaultAggregate?> FindActiveByNitAsync(Guid tenantId, string nitEmpresa, CancellationToken cancellationToken = default) =>
            Task.FromResult<SignatureVaultAggregate?>(null);

        public Task<SignatureVaultAggregate?> FindActiveByDocumentAsync(Guid tenantId, string documentType, string documentNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult<SignatureVaultAggregate?>(null);

        public Task<IReadOnlyList<SignatureVaultItem>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SignatureVaultItem>>([]);

        public Task<SignatureVaultItem?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<SignatureVaultItem?>(null);
    }

    private sealed class StubTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public StubTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
