using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// Tests del adaptador <see cref="MandateRequirementPolicy"/> (ADR-0036, HU #10912): resuelve la
/// configuración de mandato del OT por su código (join config ↔ catálogo de OT) y devuelve null cuando
/// el OT no tiene fila (⇒ default genérico + solo persona jurídica).
/// </summary>
public sealed class MandateRequirementPolicyTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid Sabaneta = Guid.Parse("ba575641-ea48-5cd2-ac51-ebba02584ba5");

    [Fact]
    public async Task ResolveAsync_ReturnsConfig_ByTransitOfficeCode()
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
            InstitutionalMandataryName = "UT-SETSA", InstitutionalMandataryNit = "900273813-7",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync(Ct);

        var policy = new MandateRequirementPolicy(ctx);
        var config = await policy.ResolveAsync("5631000", Ct);

        config.Should().NotBeNull();
        config!.TemplateCode.Should().Be("sabaneta");
        config.RequiresForNaturalPerson.Should().BeTrue();
        config.InstitutionalMandataryNit.Should().Be("900273813-7");
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNull_WhenOtHasNoConfig()
    {
        await using var ctx = NewContext();
        ctx.TransitOffices.Add(new TransitOffice
        {
            Id = Guid.NewGuid(), Code = "11001000", Name = "OTRA OT",
            DepartmentCode = "11", CityCode = "11001", IsActive = true,
        });
        await ctx.SaveChangesAsync(Ct);

        var policy = new MandateRequirementPolicy(ctx);

        (await policy.ResolveAsync("11001000", Ct)).Should().BeNull();
        (await policy.ResolveAsync("no-existe", Ct)).Should().BeNull();
        (await policy.ResolveAsync("  ", Ct)).Should().BeNull();
    }

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-mandate-config-{Guid.NewGuid()}")
            .Options);
}
