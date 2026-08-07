using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// Plantilla del OT + assignment_mode por compañía×OT (default signer).
/// </summary>
public sealed class MandateRequirementPolicyTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid Sabaneta = Guid.Parse("ba575641-ea48-5cd2-ac51-ebba02584ba5");
    private static readonly Guid CompanyA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task ResolveAsync_ReturnsTemplateFromOt_AndSignerWithoutCompanyRule()
    {
        await using var ctx = NewContext();
        ctx.TransitOffices.Add(new TransitOffice
        {
            Id = Sabaneta, Code = "5631000", Name = "STRIA TTOyTTE MCPAL SABANETA",
            DepartmentCode = "05", CityCode = "05631", IsActive = true,
        });
        ctx.TransitOfficeMandateConfigs.Add(new TransitOfficeMandateConfigEntity
        {
            Id = Guid.NewGuid(), TransitOfficeId = Sabaneta, TemplateCode = "sabaneta",
            RequiresForNaturalPerson = true,
            MandataryFamily = "organismo_transito",
            AssignmentMode = "institutional",
            InstitutionalMandataryName = "UT-SETSA", InstitutionalMandataryNit = "900273813-7",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync(Ct);

        var policy = new MandateRequirementPolicy(ctx);
        var config = await policy.ResolveAsync("5631000", CompanyA, Ct);

        config.Should().NotBeNull();
        config!.TemplateCode.Should().Be("sabaneta");
        config.RequiresForNaturalPerson.Should().BeTrue();
        config.InstitutionalMandataryNit.Should().Be("900273813-7");
        // Sin regla compañía×OT ⇒ signer (ignora assignment_mode legado del OT).
        config.AssignmentMode.Should().Be("signer");
    }

    [Fact]
    public async Task ResolveAsync_UsesCompanyRuleAssignmentMode()
    {
        await using var ctx = NewContext();
        ctx.TransitOffices.Add(new TransitOffice
        {
            Id = Sabaneta, Code = "5631000", Name = "SABANETA",
            DepartmentCode = "05", CityCode = "05631", IsActive = true,
        });
        ctx.TransitOfficeMandateConfigs.Add(new TransitOfficeMandateConfigEntity
        {
            Id = Guid.NewGuid(), TransitOfficeId = Sabaneta, TemplateCode = "sabaneta",
            RequiresForNaturalPerson = true,
            MandataryFamily = "organismo_transito",
            AssignmentMode = "signer",
            InstitutionalMandataryName = "UT-SETSA", InstitutionalMandataryNit = "900273813-7",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.CompanyOtMandateRules.Add(new CompanyOtMandateRuleEntity
        {
            Id = Guid.NewGuid(),
            CompanyTenantId = CompanyA,
            TransitOfficeId = Sabaneta,
            AssignmentMode = "institutional",
            MandataryFamily = "organismo_transito",
            InstitutionalMandataryName = "UT-SETSA Company",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync(Ct);

        var policy = new MandateRequirementPolicy(ctx);
        var config = await policy.ResolveAsync("5631000", CompanyA, Ct);

        config.Should().NotBeNull();
        config!.AssignmentMode.Should().Be("institutional");
        config.InstitutionalMandataryName.Should().Be("UT-SETSA Company");
        config.TemplateCode.Should().Be("sabaneta");
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNull_WhenOfficeUnknown()
    {
        await using var ctx = NewContext();
        var policy = new MandateRequirementPolicy(ctx);

        (await policy.ResolveAsync("no-existe", cancellationToken: Ct)).Should().BeNull();
        (await policy.ResolveAsync("  ", cancellationToken: Ct)).Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_WithoutOtConfig_ReturnsGenericoAndCompanyMode()
    {
        await using var ctx = NewContext();
        var officeId = Guid.NewGuid();
        ctx.TransitOffices.Add(new TransitOffice
        {
            Id = officeId, Code = "11001000", Name = "OTRA OT",
            DepartmentCode = "11", CityCode = "11001", IsActive = true,
        });
        ctx.CompanyOtMandateRules.Add(new CompanyOtMandateRuleEntity
        {
            Id = Guid.NewGuid(),
            CompanyTenantId = CompanyA,
            TransitOfficeId = officeId,
            AssignmentMode = "open",
            MandataryFamily = "individuo",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync(Ct);

        var policy = new MandateRequirementPolicy(ctx);
        var config = await policy.ResolveAsync("11001000", CompanyA, Ct);

        config.Should().NotBeNull();
        config!.TemplateCode.Should().Be("generico");
        config.AssignmentMode.Should().Be("open");
    }

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-mandate-config-{Guid.NewGuid()}")
            .Options);
}
