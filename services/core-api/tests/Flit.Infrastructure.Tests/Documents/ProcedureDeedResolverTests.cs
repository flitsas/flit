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
/// #10926, ADR-0033): por cada actor persona jurídica (NIT) resuelve su escritura vigente de mayor
/// vigencia y baja los bytes; tipo por rol (vendedor⇒escritura, comprador⇒escritura_comprador).
/// </summary>
public sealed class ProcedureDeedResolverTests
{
    private static readonly Guid Tenant = Guid.Parse("77777777-0000-4000-8000-0000000000d1");

    private static ProcedureInstanceActor Actor(string rol, string docType, string doc) =>
        new() { Id = Guid.NewGuid(), TenantId = Tenant, ActorType = rol, DocumentType = docType, DocumentNumber = doc, FullName = "ACME" };

    private static RepresentedCompanyItem Company(Guid id, string nit) =>
        new() { Id = id, TenantId = Tenant, DocumentType = "NIT", DocumentNumber = nit, Name = "ACME S.A.S." };

    private static DeedItem Deed(Guid id, string path, DateOnly hasta, params Guid[] companies) =>
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
            RepresentedCompanyIds = companies,
        };

    [Fact]
    public async Task Resolve_PerRole_CollapsesByVigencia_ReadsBytes()
    {
        var coVend = Guid.NewGuid();
        var coComp = Guid.NewGuid();
        var deedVend = Deed(Guid.NewGuid(), "path/vend.pdf", new DateOnly(2026, 12, 31), coVend);
        // La compañía compradora tiene DOS vigentes: debe ganar la de mayor vigencia (2027 > 2026).
        var deedCompCorta = Deed(Guid.NewGuid(), "path/comp-corta.pdf", new DateOnly(2026, 6, 30), coComp);
        var deedCompLarga = Deed(Guid.NewGuid(), "path/comp-larga.pdf", new DateOnly(2027, 6, 30), coComp);

        var reader = new FakeDeedReader([deedVend, deedCompCorta, deedCompLarga]);
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
        // Colapso por mayor vigencia: se bajó la escritura larga, no la corta.
        Encoding.UTF8.GetString(comp.Content).Should().Be("%PDF-COMP");
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
        var reader = new FakeDeedReader([Deed(Guid.NewGuid(), "p.pdf", new DateOnly(2026, 12, 31), co)]);
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
        var reader = new FakeDeedReader([Deed(Guid.NewGuid(), "p.pdf", new DateOnly(2026, 12, 31), otra)]);
        var reps = new FakeRepReader(new() { ["900000000-1"] = Company(coVend, "900000000-1") });
        var resolver = new ProcedureDeedResolver(reader, reps, new FakeStorage(new()), TimeProvider.System);

        var result = await resolver.ResolveForActorsAsync(
            Tenant, [Actor("vendedor", "NIT", "900000000-1")], CancellationToken.None);

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
    }

    private sealed class FakeRepReader(Dictionary<string, RepresentedCompanyItem> byNit) : ILegalRepresentativeReader
    {
        public Task<RepresentedCompanyItem?> FindRepresentedCompanyByNitAsync(Guid tenantId, string documentNumber, CancellationToken ct = default) =>
            Task.FromResult(byNit.TryGetValue(documentNumber, out var c) ? c : null);

        public Task<PagedResult<LegalRepresentativeItem>> ListPagedAsync(Guid tenantId, int page, int pageSize, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<LegalRepresentativeItem?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<LegalRepresentativeItem?> FindActiveByCompanyNitAndDocumentAsync(Guid tenantId, string companyNit, string documentType, string documentNumber, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<LegalRepresentativeItem?> FindActiveByCompanyNitAsync(Guid tenantId, string companyNit, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<RepresentedCompanyItem>> ListRepresentedCompaniesAsync(Guid tenantId, CancellationToken ct = default) =>
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
