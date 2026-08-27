using System.Text;
using Flit.Admin.Domain.Common;
using Flit.Admin.Domain.Companies.LegalRepresentatives;
using Flit.Infrastructure.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// Tests del resolutor de escrituras para el consolidado (<see cref="ProcedureDeedResolver"/>, HU
/// #10926, ADR-0033): por cada actor persona jurídica (NIT) resuelve su escritura vigente MÁS PRÓXIMA
/// A VENCER (menor VigenciaHasta, HU #10936) y baja los bytes; tipo por rol (vendedor⇒escritura,
/// comprador⇒escritura_comprador), con la referencia (DeedId) de la escritura elegida.
/// </summary>
public sealed class ProcedureDeedResolverTests
{
    private static readonly Guid Tenant = Guid.Parse("77777777-0000-4000-8000-0000000000d1");

    private static ProcedureInstanceActor Actor(
        string rol, string docType, string doc, string? personType = null, string? metadata = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            ActorType = rol,
            DocumentType = docType,
            DocumentNumber = doc,
            FullName = "ACME",
            PersonType = personType,
            Metadata = metadata ?? "{}",
        };

    /// <summary>Actor persona jurídica cuyo representante legal (embebido en metadata) valida identidad.</summary>
    private static ProcedureInstanceActor JuridicalActor(string rol, string nit, string rlDocType, string rlDoc) =>
        Actor(
            rol, "NIT", nit, "juridical",
            $"{{\"representanteLegal\":{{\"tipoDocumento\":\"{rlDocType}\",\"numeroDocumento\":\"{rlDoc}\"}}}}");

    private static RepresentedCompanyItem Company(Guid id, string nit) =>
        new() { Id = id, TenantId = Tenant, DocumentType = "NIT", DocumentNumber = nit, Name = "ACME S.A.S." };

    private static LegalRepresentativeItem Representative(Guid id, string docType, string doc) =>
        new() { Id = id, TenantId = Tenant, DocumentType = docType, DocumentNumber = doc, Name = "RL" };

    private static DeedItem Deed(
        Guid id, string path, DateOnly hasta, Guid[] companies, Guid? representativeId = null, DateTimeOffset? createdAt = null) =>
        new()
        {
            Id = id,
            TenantId = Tenant,
            Description = "Escritura",
            StoragePath = path,
            StorageSha256 = "sha",
            VigenciaDesde = new DateOnly(2026, 1, 1),
            VigenciaHasta = hasta,
            IsActive = true,
            RepresentativeId = representativeId,
            RepresentedCompanyIds = companies,
            CreatedAt = createdAt ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };

    [Fact]
    public async Task Resolve_PerRole_CollapsesByProximaAVencer_ReadsBytes_AndDeedId()
    {
        var coVend = Guid.NewGuid();
        var coComp = Guid.NewGuid();
        var deedVend = Deed(Guid.NewGuid(), "path/vend.pdf", new DateOnly(2026, 12, 31), [coVend], createdAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var deedCompId = Guid.NewGuid();
        var deedCompCorta = Deed(Guid.NewGuid(), "path/comp-corta.pdf", new DateOnly(2026, 6, 30), [coComp], createdAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var deedCompLarga = Deed(deedCompId, "path/comp-larga.pdf", new DateOnly(2027, 6, 30), [coComp], createdAt: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));

        var reader = new FakeDeedReader([deedVend, deedCompLarga, deedCompCorta]);
        var reps = new FakeRepReader(new()
        {
            ["900000000-1"] = Company(coVend, "900000000-1"),
            ["900000000-2"] = Company(coComp, "900000000-2"),
        });
        var storage = new FakeStorage(new()
        {
            ["path/vend.pdf"] = Encoding.UTF8.GetBytes("%PDF-VEND"),
            ["path/comp-larga.pdf"] = Encoding.UTF8.GetBytes("%PDF-COMP"),
        });

        var resolver = new ProcedureDeedResolver(reader, reps, storage, TimeProvider.System);

        var actors = new[]
        {
            Actor("vendedor", "NIT", "900000000-1"),
            Actor("comprador", "NIT", "900000000-2"),
        };

        var result = await resolver.ResolveForActorsAsync(Tenant, actors, CancellationToken.None);

        result.Should().HaveCount(2);
        var vend = result.Single(r => r.Tipo == "escritura");
        vend.Filename.Should().Be("escritura.pdf");
        vend.Nit.Should().Be("900000000-1");
        Encoding.UTF8.GetString(vend.Content).Should().Be("%PDF-VEND");

        var comp = result.Single(r => r.Tipo == "escritura_comprador");
        comp.Nit.Should().Be("900000000-2");
        Encoding.UTF8.GetString(comp.Content).Should().Be("%PDF-COMP");
        comp.DeedId.Should().Be(deedCompId);
    }

    [Fact]
    public async Task Resolve_TresVigentes_EligeMasReciente()
    {
        var co = Guid.NewGuid();
        var masReciente = Guid.NewGuid();
        var deeds = new[]
        {
            Deed(Guid.NewGuid(), "path/c.pdf", new DateOnly(2027, 12, 31), [co], createdAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            Deed(Guid.NewGuid(), "path/a.pdf", new DateOnly(2026, 3, 15), [co], createdAt: new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero)),
            Deed(masReciente, "path/b.pdf", new DateOnly(2026, 9, 30), [co], createdAt: new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)),
        };
        var reader = new FakeDeedReader(deeds);
        var reps = new FakeRepReader(new() { ["900000000-3"] = Company(co, "900000000-3") });
        var storage = new FakeStorage(new() { ["path/b.pdf"] = Encoding.UTF8.GetBytes("%PDF-B") });
        var resolver = new ProcedureDeedResolver(reader, reps, storage, TimeProvider.System);

        var result = await resolver.ResolveForActorsAsync(
            Tenant, [Actor("vendedor", "NIT", "900000000-3")], CancellationToken.None);

        result.Should().ContainSingle();
        result[0].DeedId.Should().Be(masReciente);
        Encoding.UTF8.GetString(result[0].Content).Should().Be("%PDF-B");
    }

    [Fact]
    public async Task Resolve_NoNitActors_ReturnsEmpty()
    {
        var resolver = new ProcedureDeedResolver(
            new FakeDeedReader([]), new FakeRepReader(new()), new FakeStorage(new()), TimeProvider.System);

        var result = await resolver.ResolveForActorsAsync(
            Tenant, [Actor("comprador", "CC", "123")], CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Resolve_CompanyNotInDirectory_Skips()
    {
        var co = Guid.NewGuid();
        var reader = new FakeDeedReader([Deed(Guid.NewGuid(), "p.pdf", new DateOnly(2026, 12, 31), [co])]);
        var resolver = new ProcedureDeedResolver(
            reader, new FakeRepReader(new()), new FakeStorage(new()), TimeProvider.System);

        var result = await resolver.ResolveForActorsAsync(
            Tenant, [Actor("vendedor", "NIT", "900000000-9")], CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Resolve_CompanyWithoutVigentDeed_Skips()
    {
        var coVend = Guid.NewGuid();
        var otra = Guid.NewGuid();
        // Hay escrituras vigentes, pero NINGUNA cubre la compañía del actor.
        var reader = new FakeDeedReader([Deed(Guid.NewGuid(), "p.pdf", new DateOnly(2026, 12, 31), [otra])]);
        var reps = new FakeRepReader(new() { ["900000000-1"] = Company(coVend, "900000000-1") });
        var resolver = new ProcedureDeedResolver(reader, reps, new FakeStorage(new()), TimeProvider.System);

        var result = await resolver.ResolveForActorsAsync(
            Tenant, [Actor("vendedor", "NIT", "900000000-1")], CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Resolve_TwoDeedsSameCompanyDifferentRepresentatives_UsesSelectedRepresentativeDeed()
    {
        // Feature #10929 — dos escrituras VIGENTES de la MISMA compañía pero de representantes distintos.
        // El trámite del representante A (RL embebido en metadata) debe usar la escritura de A, aunque la
        // de B esté más próxima a vencer (el filtro por representante manda sobre el colapso por vigencia).
        var co = Guid.NewGuid();
        var repA = Guid.NewGuid();
        var repB = Guid.NewGuid();
        var deedA = Deed(Guid.NewGuid(), "path/a.pdf", new DateOnly(2026, 12, 31), [co], repA);
        var deedB = Deed(Guid.NewGuid(), "path/b.pdf", new DateOnly(2026, 6, 30), [co], repB); // más próxima

        var reader = new FakeDeedReader([deedA, deedB]);
        var reps = new FakeRepReader(
            new() { ["900000000-1"] = Company(co, "900000000-1") },
            new() { ["111"] = Representative(repA, "CC", "111") });
        var storage = new FakeStorage(new() { ["path/a.pdf"] = Encoding.UTF8.GetBytes("%PDF-A") });
        var resolver = new ProcedureDeedResolver(reader, reps, storage, TimeProvider.System);

        var result = await resolver.ResolveForActorsAsync(
            Tenant, [JuridicalActor("vendedor", "900000000-1", "CC", "111")], CancellationToken.None);

        result.Should().ContainSingle();
        result[0].DeedId.Should().Be(deedA.Id);
        Encoding.UTF8.GetString(result[0].Content).Should().Be("%PDF-A");
    }

    [Fact]
    public async Task Resolve_RepresentativeResolved_LegacyDeedWithoutRepresentative_IsSkipped()
    {
        // Feature #10929 — si el representante se resuelve, las escrituras LEGADAS (RepresentativeId null)
        // NO se usan: pertenecen a la empresa, no al representante. Sin escritura del representante ⇒ skip.
        var co = Guid.NewGuid();
        var repA = Guid.NewGuid();
        var legacy = Deed(Guid.NewGuid(), "path/legacy.pdf", new DateOnly(2026, 12, 31), [co]); // sin representante

        var reader = new FakeDeedReader([legacy]);
        var reps = new FakeRepReader(
            new() { ["900000000-1"] = Company(co, "900000000-1") },
            new() { ["111"] = Representative(repA, "CC", "111") });
        var resolver = new ProcedureDeedResolver(reader, reps, new FakeStorage(new()), TimeProvider.System);

        var result = await resolver.ResolveForActorsAsync(
            Tenant, [JuridicalActor("vendedor", "900000000-1", "CC", "111")], CancellationToken.None);

        result.Should().BeEmpty();
    }

    // ── Fakes (convención del repo: sin Moq) ──────────────────────────────────────

    private sealed class FakeDeedReader(IReadOnlyList<DeedItem> vigentes) : IDeedReader
    {
        public Task<PagedResult<DeedItem>> ListPagedAsync(Guid tenantId, int page, int pageSize, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<DeedItem?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DeedItem>> ListActiveVigentesAsync(Guid tenantId, DateOnly today, CancellationToken ct = default) =>
            Task.FromResult(vigentes);

        public Task<DeedItem?> FindActiveByCompanyAsync(Guid tenantId, Guid representedCompanyId, CancellationToken ct = default) =>
            Task.FromResult(vigentes.FirstOrDefault(d => d.IsActive && d.RepresentedCompanyIds.Contains(representedCompanyId)));
    }

    private sealed class FakeRepReader(
        Dictionary<string, RepresentedCompanyItem> byNit,
        Dictionary<string, LegalRepresentativeItem>? byDoc = null) : ILegalRepresentativeReader
    {
        public Task<RepresentedCompanyItem?> FindRepresentedCompanyByNitAsync(Guid tenantId, string documentNumber, CancellationToken ct = default) =>
            Task.FromResult(byNit.TryGetValue(documentNumber, out var c) ? c : null);

        public Task<RepresentedCompanyItem?> FindActiveCompanyForRepresentativeAsync(
            Guid tenantId, Guid representativeId, string documentNumber, CancellationToken ct = default) =>
            FindRepresentedCompanyByNitAsync(tenantId, documentNumber, ct);

        // Feature #10929: el resolutor resuelve el representante por el documento del RL. Sin match
        // (byDoc null o clave ausente) → null → compat: el resolutor filtra solo por compañía.
        public Task<LegalRepresentativeItem?> FindActiveByDocumentAsync(Guid tenantId, string documentType, string documentNumber, CancellationToken ct = default) =>
            Task.FromResult(byDoc is not null && byDoc.TryGetValue(documentNumber, out var r) ? r : null);

        public Task<PagedResult<LegalRepresentativeItem>> ListPagedAsync(Guid tenantId, int page, int pageSize, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<LegalRepresentativeItem?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<LegalRepresentativeItem?> FindActiveByCompanyNitAndDocumentAsync(Guid tenantId, string companyNit, string documentType, string documentNumber, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<LegalRepresentativeItem?> FindActiveByCompanyNitAsync(Guid tenantId, string companyNit, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<LegalRepresentativeItem>> ListActiveByCompanyNitAsync(Guid tenantId, string companyNit, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<RepresentedCompanyItem>> ListRepresentedCompaniesAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, LegalRepresentativeBrief>> FindBriefByIdsAsync(Guid tenantId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeStorage(Dictionary<string, byte[]> byPath) : IAttachmentStorage
    {
        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(byPath.TryGetValue(storagePath, out var b) ? new MemoryStream(b) : null);

        public Task<StoredFile> SaveAsync(Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<PresignedUpload> CreatePresignedUploadAsync(Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public void Delete(string storagePath) { }
        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(string storagePath, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
