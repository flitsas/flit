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
/// Tests de la marca de compañía principal en el listado y detalle de representantes legales (HU #11177).
/// Cubren los tres AC:
/// <list type="bullet">
///   <item>AC1 — representante con varias compañías: exactamente UNA viene con <c>IsPrimary = true</c>.</item>
///   <item>AC2 — orden estable entre consultas: la principal aparece primero; el resto por fecha de
///     asociación ascendente, mismo orden en consultas sucesivas.</item>
///   <item>AC3 — representante con UNA sola compañía: esa compañía viene con <c>IsPrimary = true</c>.</item>
/// </list>
/// La principal es la compañía primaria registrada al alta (<c>companies[0]</c>), expuesta vía el
/// flag explícito <c>IsPrimary</c>; no se infiere de la columna denormalizada deprecada (decisión D2).
/// </summary>
public sealed class LegalRepresentativePrimaryCompanyTests
{
    private static readonly Guid Tenant = Guid.Parse("11177777-0000-4000-8000-000000011177");
    private const string DocRep = "555111222";
    private const string NitA = "900111111-1";
    private const string NitB = "800222222-2";
    private const string NitC = "700333333-3";

    private static readonly TimeProvider Clock =
        new StubTimeProvider(new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.FromHours(-5)));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── AC3 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC3_SingleCompany_IsMarkedAsPrimary()
    {
        await using var ctx = NewContext();
        var repId = await SeedRepAsync(ctx, [NitA]);

        var reader = new DbLegalRepresentativeReader(ctx, Clock);

        // Detalle
        var detail = await reader.GetByIdAsync(Tenant, repId, Ct);
        detail.Should().NotBeNull();
        detail!.Companies.Should().ContainSingle()
            .Which.IsPrimary.Should().BeTrue("la única compañía debe ser la principal");

        // Listado (mismo representante)
        var page = await reader.ListPagedAsync(Tenant, 1, 50, Ct);
        page.Items.Should().ContainSingle();
        page.Items[0].Companies.Should().ContainSingle()
            .Which.IsPrimary.Should().BeTrue("la única compañía debe ser la principal también en el listado");
    }

    // ── AC1 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_MultipleCompanies_ExactlyOneMarkedAsPrimary_AndIsTheFirstOne()
    {
        await using var ctx = NewContext();
        var repId = await SeedRepAsync(ctx, [NitA, NitB, NitC]);

        var reader = new DbLegalRepresentativeReader(ctx, Clock);
        var detail = await reader.GetByIdAsync(Tenant, repId, Ct);

        detail.Should().NotBeNull();
        var companies = detail!.Companies;
        companies.Should().HaveCount(3);

        // Exactamente una es principal.
        companies.Count(c => c.IsPrimary).Should().Be(1,
            "exactamente una compañía debe tener IsPrimary = true");

        // La principal es la primera de la lista.
        companies[0].IsPrimary.Should().BeTrue("la compañía principal debe aparecer primero");
        companies[0].Nit.Should().Be(NitA,
            "NitA fue la primera en el alta y debe ser la principal");

        // Las secundarias no son principales.
        companies.Skip(1).Should().AllSatisfy(c =>
            c.IsPrimary.Should().BeFalse("solo la primera compañía es principal"));
    }

    [Fact]
    public async Task AC1_MultipleCompanies_ExactlyOneMarkedAsPrimary_InListado()
    {
        await using var ctx = NewContext();
        await SeedRepAsync(ctx, [NitA, NitB]);

        var reader = new DbLegalRepresentativeReader(ctx, Clock);
        var page = await reader.ListPagedAsync(Tenant, 1, 50, Ct);

        var rep = page.Items.Should().ContainSingle().Subject;
        rep.Companies.Should().HaveCount(2);

        rep.Companies.Count(c => c.IsPrimary).Should().Be(1,
            "exactamente una compañía debe tener IsPrimary = true en el listado");
        rep.Companies[0].IsPrimary.Should().BeTrue("la principal debe aparecer primero en el listado");
        rep.Companies[0].Nit.Should().Be(NitA);
        rep.Companies[1].IsPrimary.Should().BeFalse();
    }

    // ── AC2 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC2_StableOrderAcrossQueries_PrimaryFirst()
    {
        await using var ctx = NewContext();
        var repId = await SeedRepAsync(ctx, [NitA, NitB, NitC]);

        var reader = new DbLegalRepresentativeReader(ctx, Clock);

        var first = await reader.GetByIdAsync(Tenant, repId, Ct);
        var second = await reader.GetByIdAsync(Tenant, repId, Ct);

        first.Should().NotBeNull();
        second.Should().NotBeNull();

        var nitsFirst = first!.Companies.Select(c => c.Nit).ToList();
        var nitsSecond = second!.Companies.Select(c => c.Nit).ToList();

        nitsFirst.Should().Equal(nitsSecond,
            "el orden de las compañías debe ser idéntico en consultas sucesivas");

        // La principal va primero en ambas.
        first.Companies[0].IsPrimary.Should().BeTrue("la principal debe ser la primera en la primera consulta");
        second.Companies[0].IsPrimary.Should().BeTrue("la principal debe ser la primera en la segunda consulta");
        first.Companies[0].Nit.Should().Be(NitA);
        second.Companies[0].Nit.Should().Be(NitA);
    }

    [Fact]
    public async Task AC2_SecondaryCompanies_OrderedByAssociationDate()
    {
        // Tres compañías sembradas secuencialmente: NitA = primaria, NitB y NitC = secundarias.
        // El orden estable esperado de las secundarias es por fecha de asociación: NitB < NitC.
        await using var ctx = NewContext();
        var repId = await SeedRepAsync(ctx, [NitA, NitB, NitC]);

        var reader = new DbLegalRepresentativeReader(ctx, Clock);
        var detail = await reader.GetByIdAsync(Tenant, repId, Ct);

        detail.Should().NotBeNull();
        var companies = detail!.Companies;
        companies.Should().HaveCount(3);

        companies[0].Nit.Should().Be(NitA, "NitA es la principal y va primera");
        // NitB y NitC se asociaron en ese orden → deben aparecer en ese orden.
        var secondaryNits = companies.Skip(1).Select(c => c.Nit).ToList();
        secondaryNits.Should().ContainInOrder(
            new[] { NitB, NitC },
            "las secundarias deben aparecer en orden de asociación ascendente");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Crea un representante con las compañías indicadas (la primera es la primaria) y
    /// devuelve su id. Usa el handler real sobre InMemory para ejercitar el writer completo.
    /// </summary>
    private static async Task<Guid> SeedRepAsync(FlitDbContext ctx, IReadOnlyList<string> nits)
    {
        var reader = new DbLegalRepresentativeReader(ctx, Clock);
        var repo = new LegalRepresentativeRepository(ctx);
        var writer = new LegalRepresentativeWriter(
            new AlwaysExistsProcedureTypeCatalog(),
            new NullSignatureResolver(),
            new NullSignatureVaultReader(),
            repo, reader, Clock);

        var result = await new CreateLegalRepresentativeHandler(writer).HandleAsync(
            new CreateLegalRepresentativeCommand
            {
                TenantId = Tenant,
                DocumentType = "CC",
                DocumentNumber = DocRep,
                FirstLastName = "Lopez",
                Name = "Maria Lopez",
                Companies = [.. nits.Select(n => new LegalRepresentativeCompanyInput(n, $"Empresa {n}", null, null, null, null))],
            }, Ct);

        result.IsValid.Should().BeTrue("la creación del representante no debe fallar en el seed");
        return result.Id!.Value;
    }

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-rep-primary-{Guid.NewGuid()}")
            .Options);

    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class AlwaysExistsProcedureTypeCatalog : IProcedureTypeCatalog
    {
        public Task<bool> ExistsAsync(Guid procedureTypeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<IReadOnlyList<ProcedureTypeCatalogItem>> ListActivePublishedAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProcedureTypeCatalogItem>>([]);
    }

    private sealed class NullSignatureResolver : ILegalRepresentativeSignatureResolver
    {
        public Task<LegalRepresentativeSignatureResolution> ResolveAsync(
            Guid tenantId, string nitCompania, string tipoDocumento, string documentoRepresentante,
            DateOnly today, CancellationToken cancellationToken = default) =>
            Task.FromResult(LegalRepresentativeSignatureResolution.None);
    }

    private sealed class NullSignatureVaultReader : ISignatureVaultReader
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
