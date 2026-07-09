using Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.ListOtCompanies;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

using Scenario = MandateSignerHandlerTests;

/// <summary>
/// Tests de la regla de uso RF33 (compañía activa y no bloqueada en el OT) y de la vista
/// consolidada RF34/RF26 (ADR-0023, HU10614). Reutilizan el seed InMemory de
/// <see cref="MandateSignerHandlerTests"/> y ejercitan <see cref="CreateMandateSignerHandler"/>
/// y <see cref="ListOtCompaniesHandler"/> reales.
/// </summary>
public sealed class MandateSignerUsageAndViewTests
{
    [Fact]
    public async Task Create_RF33_RejectsBlockedOrInactiveCompany()
    {
        await using var ctx = Scenario.NewSeededContext();
        // CompanyC: grant deshabilitado (bloqueada en el OT).
        ctx.TenantTransitOfficeGrants.Single(g => g.TenantId == Scenario.CompanyC).IsEnabled = false;
        await ctx.SaveChangesAsync(Scenario.Ct);

        var create = new CreateMandateSignerHandler(
            new DbTransitOfficeOperationalStatusReader(ctx),
            new DbMandateSignerReader(ctx),
            new MandateSignerRepository(ctx));

        var result = await create.HandleAsync(
            Scenario.NewCreate("Samuel", "111", [Scenario.CompanyC]), Scenario.Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.Field == "companyTenantIds"
            && e.Value == Scenario.CompanyC.ToString()
            && e.Message.Contains("no está habilitada o está inactiva"));
    }

    [Fact]
    public async Task Create_RF34_ResolvesSignerPerCompany_InConsolidatedView()
    {
        await using var ctx = Scenario.NewSeededContext();
        var (create, companies) = Handlers(ctx);

        await create.HandleAsync(Scenario.NewCreate("Samuel Cárdenas", "111", [Scenario.CompanyA]), Scenario.Ct);

        var view = await companies.HandleAsync(
            new ListOtCompaniesQuery { TransitOfficeId = Scenario.Office }, Scenario.Ct);

        view.Single(c => c.CompanyTenantId == Scenario.CompanyA).AssignedSignerName
            .Should().Be("Samuel Cárdenas");
    }

    [Fact]
    public async Task ListOtCompanies_RF26_ReportsCompaniesWithoutSigner()
    {
        await using var ctx = Scenario.NewSeededContext();
        var (create, companies) = Handlers(ctx);

        await create.HandleAsync(Scenario.NewCreate("Samuel", "111", [Scenario.CompanyA]), Scenario.Ct);

        var view = await companies.HandleAsync(
            new ListOtCompaniesQuery { TransitOfficeId = Scenario.Office }, Scenario.Ct);

        // B y C no tienen mandatario (RF26: se advertirá al generar su mandato).
        view.Single(c => c.CompanyTenantId == Scenario.CompanyB).AssignedSignerId.Should().BeNull();
        view.Single(c => c.CompanyTenantId == Scenario.CompanyC).AssignedSignerId.Should().BeNull();
        view.Single(c => c.CompanyTenantId == Scenario.CompanyA).AssignedSignerId.Should().NotBeNull();
    }

    private static (CreateMandateSignerHandler Create, ListOtCompaniesHandler Companies) Handlers(
        Flit.Infrastructure.Persistence.FlitDbContext ctx)
    {
        var reader = new DbMandateSignerReader(ctx);
        return (
            new CreateMandateSignerHandler(
                new DbTransitOfficeOperationalStatusReader(ctx), reader, new MandateSignerRepository(ctx)),
            new ListOtCompaniesHandler(reader));
    }
}
