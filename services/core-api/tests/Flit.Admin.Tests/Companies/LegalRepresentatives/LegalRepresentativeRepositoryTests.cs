using Flit.Admin.Domain.Companies.LegalRepresentatives;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.LegalRepresentatives;

/// <summary>
/// Tests de integración (InMemory) de los repos/readers del directorio de representantes legales
/// (HU #10900, ADR-0033): upsert de compañía por NIT, alta/edición de representante con tipos de
/// trámite y vínculo de firma, listado paginado, lookup por NIT+documento y escrituras vigentes.
/// </summary>
public sealed class LegalRepresentativeRepositoryTests
{
    private static readonly Guid Tenant = Guid.Parse("77777777-0000-4000-8000-00000000cc01");
    private static readonly Guid OtherTenant = Guid.Parse("77777777-0000-4000-8000-00000000cc99");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-legal-rep-{Guid.NewGuid()}")
            .Options);

    [Fact]
    public async Task UpsertRepresentedCompany_ByNit_IsIdempotent_AndUpdatesDetails()
    {
        await using var ctx = NewContext();
        var repo = new LegalRepresentativeRepository(ctx);
        var reader = new DbLegalRepresentativeReader(ctx);

        var firstId = await repo.UpsertRepresentedCompanyAsync(
            new UpsertRepresentedCompanyData(Tenant, "900000000-1", "ACME S.A.S.", "a@x.co", null, null, null, null), Ct);

        // Mismo NIT → misma fila (upsert), con datos actualizados.
        var secondId = await repo.UpsertRepresentedCompanyAsync(
            new UpsertRepresentedCompanyData(Tenant, "900000000-1", "ACME Renting S.A.S.", "b@x.co", "Cll 1", "Bogotá", "3001112233", null), Ct);

        secondId.Should().Be(firstId);

        var companies = await reader.ListRepresentedCompaniesAsync(Tenant, Ct);
        companies.Should().ContainSingle();
        companies[0].Name.Should().Be("ACME Renting S.A.S.");
        companies[0].Email.Should().Be("b@x.co");
        companies[0].City.Should().Be("Bogotá");
    }

    [Fact]
    public async Task Save_CreatesRepresentative_WithProcedureTypes_AndSignatureLink()
    {
        await using var ctx = NewContext();
        var repo = new LegalRepresentativeRepository(ctx);
        var reader = new DbLegalRepresentativeReader(ctx);

        var companyId = await repo.UpsertRepresentedCompanyAsync(
            new UpsertRepresentedCompanyData(Tenant, "900000000-1", "ACME S.A.S.", null, null, null, null, null), Ct);

        var signatureId = Guid.NewGuid();
        var procType = Guid.NewGuid();
        var repId = await repo.SaveAsync(new SaveLegalRepresentativeData(
            Tenant, Id: null, companyId, "CC", "123456789", "Perez", "Gomez", "Juan Perez",
            "juan@x.co", null, null, null, SignatureVaultId: signatureId, IdentityValidationRef: null,
            ProcedureTypeIds: [procType], ActorBy: null), Ct);

        var item = await reader.GetByIdAsync(Tenant, repId, Ct);
        item.Should().NotBeNull();
        item!.CompanyName.Should().Be("ACME S.A.S.");
        item.CompanyDocumentNumber.Should().Be("900000000-1");
        item.SignatureVaultId.Should().Be(signatureId);
        item.HasSignatureOrIdentity.Should().BeTrue();
        item.ProcedureTypeIds.Should().ContainSingle().Which.Should().Be(procType);
        item.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Save_Edit_ReplacesProcedureTypes_AndSwitchesToIdentity()
    {
        await using var ctx = NewContext();
        var repo = new LegalRepresentativeRepository(ctx);
        var reader = new DbLegalRepresentativeReader(ctx);

        var companyId = await repo.UpsertRepresentedCompanyAsync(
            new UpsertRepresentedCompanyData(Tenant, "900000000-1", "ACME S.A.S.", null, null, null, null, null), Ct);
        var type1 = Guid.NewGuid();
        var repId = await repo.SaveAsync(new SaveLegalRepresentativeData(
            Tenant, null, companyId, "CC", "123456789", "Perez", null, "Juan Perez",
            null, null, null, null, Guid.NewGuid(), null, [type1], null), Ct);

        // Edición: nuevos tipos + cambia a identidad (limpia la firma).
        var identityRef = Guid.NewGuid();
        var type2 = Guid.NewGuid();
        await repo.SaveAsync(new SaveLegalRepresentativeData(
            Tenant, repId, companyId, "CC", "123456789", "Perez", null, "Juan A. Perez",
            null, null, null, null, SignatureVaultId: null, IdentityValidationRef: identityRef,
            ProcedureTypeIds: [type2], ActorBy: null), Ct);

        var item = await reader.GetByIdAsync(Tenant, repId, Ct);
        item!.Name.Should().Be("Juan A. Perez");
        item.SignatureVaultId.Should().BeNull();
        item.IdentityValidationRef.Should().Be(identityRef);
        item.ProcedureTypeIds.Should().ContainSingle().Which.Should().Be(type2);
    }

    [Fact]
    public async Task ListPaged_ReturnsTenantScopedItems_WithTotalCount()
    {
        await using var ctx = NewContext();
        var repo = new LegalRepresentativeRepository(ctx);
        var reader = new DbLegalRepresentativeReader(ctx);

        var companyId = await repo.UpsertRepresentedCompanyAsync(
            new UpsertRepresentedCompanyData(Tenant, "900000000-1", "ACME S.A.S.", null, null, null, null, null), Ct);
        for (var i = 0; i < 3; i++)
        {
            await repo.SaveAsync(new SaveLegalRepresentativeData(
                Tenant, null, companyId, "CC", $"10000000{i}", "Perez", null, $"Rep {i}",
                null, null, null, null, null, null, [], null), Ct);
        }

        // Otro tenant no debe aparecer (aislamiento; en InMemory por filtro tenant_id).
        var otherCompany = await repo.UpsertRepresentedCompanyAsync(
            new UpsertRepresentedCompanyData(OtherTenant, "800000000-1", "Otra S.A.S.", null, null, null, null, null), Ct);
        await repo.SaveAsync(new SaveLegalRepresentativeData(
            OtherTenant, null, otherCompany, "CC", "222222222", "X", null, "Ajeno",
            null, null, null, null, null, null, [], null), Ct);

        var page = await reader.ListPagedAsync(Tenant, page: 1, pageSize: 2, Ct);
        page.TotalCount.Should().Be(3);
        page.Items.Should().HaveCount(2);
        page.Items.Should().OnlyContain(r => r.TenantId == Tenant);
    }

    [Fact]
    public async Task FindActiveByCompanyNitAndDocument_MatchesActiveOnly()
    {
        await using var ctx = NewContext();
        var repo = new LegalRepresentativeRepository(ctx);
        var reader = new DbLegalRepresentativeReader(ctx);

        var companyId = await repo.UpsertRepresentedCompanyAsync(
            new UpsertRepresentedCompanyData(Tenant, "900000000-1", "ACME S.A.S.", null, null, null, null, null), Ct);
        var repId = await repo.SaveAsync(new SaveLegalRepresentativeData(
            Tenant, null, companyId, "CC", "123456789", "Perez", null, "Juan Perez",
            null, null, null, null, null, null, [], null), Ct);

        var found = await reader.FindActiveByCompanyNitAndDocumentAsync(Tenant, "900000000-1", "CC", "123456789", Ct);
        found.Should().NotBeNull();
        found!.Id.Should().Be(repId);

        // Tras baja lógica ya no hace match.
        await repo.DeactivateAsync(Tenant, repId, null, Ct);
        var afterDeactivate = await reader.FindActiveByCompanyNitAndDocumentAsync(Tenant, "900000000-1", "CC", "123456789", Ct);
        afterDeactivate.Should().BeNull();
    }

    [Fact]
    public async Task Deeds_ListActiveVigentes_FiltersByActiveAndVigencia()
    {
        await using var ctx = NewContext();
        var repo = new DeedRepository(ctx);
        var reader = new DbDeedReader(ctx);

        var today = new DateOnly(2026, 7, 23);
        var companyId = await new LegalRepresentativeRepository(ctx).UpsertRepresentedCompanyAsync(
            new UpsertRepresentedCompanyData(Tenant, "900000000-1", "ACME S.A.S.", null, null, null, null, null), Ct);

        var vigente = await repo.CreateAsync(new SaveDeedData(
            Tenant, null, "Vigente", "path-1", "sha-1",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), [companyId], null), Ct);
        // Vencida (fuera de vigencia).
        await repo.CreateAsync(new SaveDeedData(
            Tenant, null, "Vencida", "path-2", "sha-2",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), [], null), Ct);
        // Activa y vigente pero luego dada de baja.
        var baja = await repo.CreateAsync(new SaveDeedData(
            Tenant, null, "Baja", "path-3", "sha-3",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), [], null), Ct);
        await repo.DeactivateAsync(Tenant, baja, null, Ct);

        var vigentes = await reader.ListActiveVigentesAsync(Tenant, today, Ct);

        vigentes.Should().ContainSingle();
        vigentes[0].Id.Should().Be(vigente);
        vigentes[0].Description.Should().Be("Vigente");
        vigentes[0].RepresentedCompanyIds.Should().ContainSingle().Which.Should().Be(companyId);
    }
}
