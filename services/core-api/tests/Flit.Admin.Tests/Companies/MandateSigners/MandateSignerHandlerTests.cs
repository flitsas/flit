using Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.InactivateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.ListMandateSigners;
using Flit.Admin.Application.Companies.MandateSigners.ReactivateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.UpdateMandateSigner;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// Tests del CRUD de mandatarios y la exclusividad estricta (ADR-0023, HU10613) ejercitando
/// los handlers reales sobre <see cref="DbMandateSignerReader"/> + <see cref="MandateSignerRepository"/>
/// + <see cref="DbTransitOfficeOperationalStatusReader"/> con proveedor InMemory. Cubren alta,
/// exclusividad (compañía ya tomada por otro mandatario activo), soft-delete que libera
/// compañías y regeneración de huella al editar. El seed y las constantes se comparten con
/// <see cref="MandateSignerUsageAndViewTests"/> (HU10614, RF33/RF34).
/// </summary>
public sealed class MandateSignerHandlerTests
{
    internal static readonly Guid Office = Guid.Parse("0ff1ce00-0000-4000-8000-000000000001");
    internal static readonly Guid OtTenant = Guid.Parse("07107107-0000-4000-8000-000000000001");
    internal static readonly Guid CompanyA = Guid.Parse("c0a0a0a0-0000-4000-8000-00000000000a");
    internal static readonly Guid CompanyB = Guid.Parse("c0b0b0b0-0000-4000-8000-00000000000b");
    internal static readonly Guid CompanyC = Guid.Parse("c0c0c0c0-0000-4000-8000-00000000000c");
    internal static readonly Guid Operator = Guid.Parse("09e12a70-0000-4000-8000-000000000001");

    [Fact]
    public async Task Create_HappyPath_PersistsSignerHashAndCompanies()
    {
        await using var ctx = NewSeededContext();
        var (create, _, _, _, list) = CrudHandlers(ctx);

        var result = await create.HandleAsync(NewCreate("Samuel Cárdenas", "123456", [CompanyA, CompanyB]), Ct);

        result.IsValid.Should().BeTrue();
        result.MandateSignerId.Should().NotBeNull();
        result.IntegrityHash.Should().MatchRegex("^[0-9a-f]{64}$");

        var signers = await list.HandleAsync(new ListMandateSignersQuery { TransitOfficeId = Office }, Ct);
        signers.Should().ContainSingle();
        signers[0].FullName.Should().Be("Samuel Cárdenas");
        signers[0].CompanyTenantIds.Should().BeEquivalentTo([CompanyA, CompanyB]);
    }

    [Fact]
    public async Task Create_RejectsCompanyAlreadyTakenByAnotherSigner_Exclusivity()
    {
        await using var ctx = NewSeededContext();
        var (create, _, _, _, _) = CrudHandlers(ctx);

        var firstCreate = await create.HandleAsync(NewCreate("Samuel", "111", [CompanyA]), Ct);
        firstCreate.IsValid.Should().BeTrue();

        // Daniel NO puede tomar A (ya es de Samuel).
        var second = await create.HandleAsync(NewCreate("Daniel", "222", [CompanyA, CompanyB]), Ct);

        second.IsValid.Should().BeFalse();
        second.Errors.Should().Contain(e =>
            e.Field == "companyTenantIds"
            && e.Value == CompanyA.ToString()
            && e.Message.Contains("ya tiene un mandatario"));
    }

    [Fact]
    public async Task Inactivate_FreesCompanies_ButKeepsSignerVisible_SoftDelete()
    {
        await using var ctx = NewSeededContext();
        var (create, _, inactivate, _, list) = CrudHandlers(ctx);

        var samuel = await create.HandleAsync(NewCreate("Samuel", "111", [CompanyA]), Ct);

        // Antes de inactivar, Daniel no puede tomar A.
        var blocked = await create.HandleAsync(NewCreate("Daniel", "222", [CompanyA]), Ct);
        blocked.IsValid.Should().BeFalse();

        var outcome = await inactivate.HandleAsync(new InactivateMandateSignerCommand
        {
            TransitOfficeId = Office,
            MandateSignerId = samuel.MandateSignerId!.Value,
            ChangedBy = Operator,
        }, Ct);
        outcome.Should().Be(InactivateMandateSignerOutcome.Inactivated);

        // Samuel SIGUE visible en el listado, pero inactivo y sin compañías (liberadas).
        var signers = await list.HandleAsync(new ListMandateSignersQuery { TransitOfficeId = Office }, Ct);
        var samuelRow = signers.Single(s => s.Id == samuel.MandateSignerId);
        samuelRow.IsActive.Should().BeFalse();
        samuelRow.CompanyTenantIds.Should().BeEmpty();

        // Ahora A queda libre → Daniel sí puede tomarla.
        var freed = await create.HandleAsync(NewCreate("Daniel", "222", [CompanyA]), Ct);
        freed.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Reactivate_BringsSignerBackActive_WithoutCompanies()
    {
        await using var ctx = NewSeededContext();
        var (create, _, inactivate, reactivate, list) = CrudHandlers(ctx);

        var samuel = await create.HandleAsync(NewCreate("Samuel", "111", [CompanyA]), Ct);
        await inactivate.HandleAsync(new InactivateMandateSignerCommand
        {
            TransitOfficeId = Office,
            MandateSignerId = samuel.MandateSignerId!.Value,
            ChangedBy = Operator,
        }, Ct);

        var outcome = await reactivate.HandleAsync(new ReactivateMandateSignerCommand
        {
            TransitOfficeId = Office,
            MandateSignerId = samuel.MandateSignerId!.Value,
            ChangedBy = Operator,
        }, Ct);
        outcome.Should().Be(ReactivateMandateSignerOutcome.Reactivated);

        // Vuelve activo pero sin compañías (se liberaron y no se restauran).
        var row = (await list.HandleAsync(new ListMandateSignersQuery { TransitOfficeId = Office }, Ct))
            .Single(s => s.Id == samuel.MandateSignerId);
        row.IsActive.Should().BeTrue();
        row.CompanyTenantIds.Should().BeEmpty();

        // Reactivar de nuevo es idempotente (ya activo) → NotFound.
        var again = await reactivate.HandleAsync(new ReactivateMandateSignerCommand
        {
            TransitOfficeId = Office,
            MandateSignerId = samuel.MandateSignerId!.Value,
            ChangedBy = Operator,
        }, Ct);
        again.Should().Be(ReactivateMandateSignerOutcome.NotFound);
    }

    [Fact]
    public async Task Update_RegeneratesHash_AndKeepsRegisteredAt()
    {
        await using var ctx = NewSeededContext();
        var (create, update, _, _, list) = CrudHandlers(ctx);

        var created = await create.HandleAsync(NewCreate("Samuel", "123456", [CompanyA]), Ct);
        var before = (await list.HandleAsync(new ListMandateSignersQuery { TransitOfficeId = Office }, Ct)).Single();

        var updateResult = await update.HandleAsync(new UpdateMandateSignerCommand
        {
            TransitOfficeId = Office,
            MandateSignerId = created.MandateSignerId!.Value,
            FullName = "Samuel Alberto Cárdenas",
            DocumentNumber = "123456",
            CompanyTenantIds = [CompanyA, CompanyB],
            UpdatedBy = Operator,
        }, Ct);

        updateResult.Outcome.Should().Be(UpdateMandateSignerOutcome.Updated);

        var after = (await list.HandleAsync(new ListMandateSignersQuery { TransitOfficeId = Office }, Ct)).Single();
        after.IntegrityHash.Should().NotBe(before.IntegrityHash); // regenerada.
        after.RegisteredAt.Should().Be(before.RegisteredAt);      // fecha de registro intacta.
        after.CompanyTenantIds.Should().BeEquivalentTo([CompanyA, CompanyB]);
    }

    [Fact]
    public async Task Update_AllowsKeepingOwnCompany_NotAConflict()
    {
        await using var ctx = NewSeededContext();
        var (create, update, _, _, _) = CrudHandlers(ctx);

        var created = await create.HandleAsync(NewCreate("Samuel", "111", [CompanyA]), Ct);

        // Editar manteniendo A (ya suya) + agregar B no debe chocar por exclusividad.
        var updated = await update.HandleAsync(new UpdateMandateSignerCommand
        {
            TransitOfficeId = Office,
            MandateSignerId = created.MandateSignerId!.Value,
            FullName = "Samuel",
            DocumentNumber = "111",
            CompanyTenantIds = [CompanyA, CompanyB],
            UpdatedBy = Operator,
        }, Ct);

        updated.Outcome.Should().Be(UpdateMandateSignerOutcome.Updated);
    }

    [Fact]
    public async Task Create_RejectsWhenOtIsInactive_Contract()
    {
        await using var ctx = NewSeededContext();
        ctx.Tenants.Single(t => t.Id == OtTenant).IsActive = false; // OT inactivo en FLIT.
        await ctx.SaveChangesAsync(Ct);

        var (create, _, _, _, _) = CrudHandlers(ctx);

        var result = await create.HandleAsync(NewCreate("Samuel", "111", [CompanyA]), Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "transitOfficeId");
    }

    // ---------- Helpers compartidos ----------

    internal static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static CreateMandateSignerCommand NewCreate(
        string fullName, string documentNumber, IReadOnlyList<Guid> companies) =>
        new()
        {
            TransitOfficeId = Office,
            FullName = fullName,
            DocumentNumber = documentNumber,
            CompanyTenantIds = companies,
            CreatedBy = Operator,
        };

    private static (
        CreateMandateSignerHandler Create,
        UpdateMandateSignerHandler Update,
        InactivateMandateSignerHandler Inactivate,
        ReactivateMandateSignerHandler Reactivate,
        ListMandateSignersHandler List) CrudHandlers(FlitDbContext ctx)
    {
        var otStatus = new DbTransitOfficeOperationalStatusReader(ctx);
        var reader = new DbMandateSignerReader(ctx);
        var repo = new MandateSignerRepository(ctx);
        return (
            new CreateMandateSignerHandler(otStatus, reader, repo),
            new UpdateMandateSignerHandler(otStatus, reader, repo),
            new InactivateMandateSignerHandler(otStatus, reader, repo),
            new ReactivateMandateSignerHandler(otStatus, reader, repo),
            new ListMandateSignersHandler(reader));
    }

    internal static FlitDbContext NewSeededContext()
    {
        var ctx = new FlitDbContext(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-mandate-signers-{Guid.NewGuid()}")
            .Options);

        ctx.TransitOffices.Add(new TransitOffice
        {
            Id = Office, Code = "11001", Name = "Secretaría de Movilidad",
            DepartmentCode = "11", CityCode = "11001", IsActive = true,
        });

        ctx.Tenants.AddRange(
            NewTenant(OtTenant, "OT-01", "Organismo de Tránsito S.A.S.", "900000000-1"),
            NewTenant(CompanyA, "CIA-A", "Compañía A S.A.S.", "900000001-1"),
            NewTenant(CompanyB, "CIA-B", "Compañía B S.A.S.", "900000002-1"),
            NewTenant(CompanyC, "CIA-C", "Compañía C S.A.S.", "900000003-1"));

        ctx.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(), TenantId = OtTenant, TransitOfficeId = Office,
            OperationMode = "dashboard", CreatedAt = DateTimeOffset.UtcNow,
        });

        foreach (var company in new[] { CompanyA, CompanyB, CompanyC })
        {
            ctx.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
            {
                Id = Guid.NewGuid(), TenantId = company, TransitOfficeId = Office,
                IsEnabled = true, CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        ctx.SaveChanges();
        return ctx;
    }

    private static Tenant NewTenant(Guid id, string code, string legalName, string taxId) =>
        new()
        {
            Id = id, Code = code, LegalName = legalName, TaxId = taxId,
            TenantType = "RENTING", IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
        };
}
