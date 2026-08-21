using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Tramites.Domain.Documents;
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
    public async Task ResolveAsync_ExposesDefaultMandateSigner_WhenSignerMode()
    {
        await using var ctx = NewContext();
        var signerId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        ctx.TransitOffices.Add(new TransitOffice
        {
            Id = Sabaneta, Code = "5631000", Name = "SABANETA",
            DepartmentCode = "05", CityCode = "05631", IsActive = true,
        });
        ctx.TransitOfficeMandateConfigs.Add(new TransitOfficeMandateConfigEntity
        {
            Id = Guid.NewGuid(), TransitOfficeId = Sabaneta, TemplateCode = "generico",
            RequiresForNaturalPerson = false,
            MandataryFamily = "individuo",
            AssignmentMode = "signer",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.CompanyOtMandateRules.Add(new CompanyOtMandateRuleEntity
        {
            Id = Guid.NewGuid(),
            CompanyTenantId = CompanyA,
            TransitOfficeId = Sabaneta,
            AssignmentMode = "signer",
            MandataryFamily = "individuo",
            DefaultMandateSignerId = signerId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync(Ct);

        var policy = new MandateRequirementPolicy(ctx);
        var config = await policy.ResolveAsync("5631000", CompanyA, Ct);

        config!.AssignmentMode.Should().Be("signer");
        config.DefaultMandateSignerId.Should().Be(signerId);
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

    [Fact]
    public async Task ResolveAsync_SabanetaSinConfig_UsaPlantillaDeSistema()
    {
        await using var ctx = NewContext();
        ctx.TransitOffices.Add(new TransitOffice
        {
            Id = Sabaneta, Code = "5631000", Name = "SABANETA",
            DepartmentCode = "05", CityCode = "05631", IsActive = true,
        });
        await ctx.SaveChangesAsync(Ct);

        var config = await new MandateRequirementPolicy(ctx).ResolveAsync("5631000", CompanyA, Ct);

        config!.TemplateCode.Should().Be("sabaneta");
        config.InstitutionalMandataryNit.Should().Be("900273813-7");
        MandatoCustomTemplateKindCodes.HasCustom(config.CustomTemplateKind).Should().BeFalse();
    }

    [Fact]
    public async Task ResolveAsync_BelloSinConfig_UsaPlantillaDeSistema()
    {
        await using var ctx = NewContext();
        var belloId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        ctx.TransitOffices.Add(new TransitOffice
        {
            Id = belloId, Code = "5088000", Name = "BELLO",
            DepartmentCode = "05", CityCode = "05088", IsActive = true,
        });
        await ctx.SaveChangesAsync(Ct);

        var config = await new MandateRequirementPolicy(ctx).ResolveAsync("5088000", CompanyA, Ct);

        config!.TemplateCode.Should().Be("bello");
        config.InstitutionalMandataryNit.Should().Be("901783814-6");
    }

    [Fact]
    public async Task ResolveAsync_EnvigadoSinConfig_UsaPlantillaMunicipio()
    {
        await using var ctx = NewContext();
        var envigadoId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        ctx.TransitOffices.Add(new TransitOffice
        {
            Id = envigadoId, Code = "5266000", Name = "ENVIGADO",
            DepartmentCode = "05", CityCode = "05266", IsActive = true,
        });
        await ctx.SaveChangesAsync(Ct);

        var config = await new MandateRequirementPolicy(ctx).ResolveAsync("5266000", CompanyA, Ct);

        config!.TemplateCode.Should().Be("municipio");
        config.MandataryFamily.Should().Be("individuo");
        MandatoCustomTemplateKindCodes.HasCustom(config.CustomTemplateKind).Should().BeFalse();
    }

    [Fact]
    public async Task ResolveAsync_SabanetaConPlantillaPropia_PriorizaCustom()
    {
        await using var ctx = NewContext();
        ctx.TransitOffices.Add(new TransitOffice
        {
            Id = Sabaneta, Code = "5631000", Name = "SABANETA",
            DepartmentCode = "05", CityCode = "05631", IsActive = true,
        });
        ctx.TransitOfficeMandateConfigs.Add(new TransitOfficeMandateConfigEntity
        {
            Id = Guid.NewGuid(),
            TransitOfficeId = Sabaneta,
            TemplateCode = "sabaneta",
            RequiresForNaturalPerson = true,
            MandataryFamily = "organismo_transito",
            CustomTemplateKind = "pdf",
            CustomTemplateFileName = "propia.pdf",
            CustomTemplateStoragePath = "mandatos/sabaneta.pdf",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync(Ct);

        var config = await new MandateRequirementPolicy(ctx).ResolveAsync("5631000", CompanyA, Ct);

        config!.CustomTemplateKind.Should().Be("pdf");
        config.CustomTemplateFileName.Should().Be("propia.pdf");
        config.TemplateCode.Should().Be("sabaneta");
    }

    [Fact]
    public async Task ResolveAsync_SabanetaConConfigGenerico_GanaLaEleccionDelOt()
    {
        // HU #11703 — la elección explícita del OT manda sobre la plantilla de sistema de su código.
        // Antes ganaba el builtin, y por eso configurar estos cinco organismos no tenía ningún efecto.
        await using var ctx = NewContext();
        ctx.TransitOffices.Add(new TransitOffice
        {
            Id = Sabaneta, Code = "5631000", Name = "SABANETA",
            DepartmentCode = "05", CityCode = "05631", IsActive = true,
        });
        ctx.TransitOfficeMandateConfigs.Add(new TransitOfficeMandateConfigEntity
        {
            Id = Guid.NewGuid(),
            TransitOfficeId = Sabaneta,
            TemplateCode = "generico",
            RequiresForNaturalPerson = false,
            MandataryFamily = "individuo",
            CustomTemplateKind = "none",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync(Ct);

        var config = await new MandateRequirementPolicy(ctx).ResolveAsync("5631000", CompanyA, Ct);

        config!.TemplateCode.Should().Be("generico");
        config.CustomTemplateKind.Should().Be("none");
    }

    [Fact]
    public async Task ResolveAsync_SabanetaEnAuto_DelegaEnLaPlantillaDelSistema()
    {
        await using var ctx = NewContext();
        ctx.TransitOffices.Add(new TransitOffice
        {
            Id = Sabaneta, Code = "5631000", Name = "SABANETA",
            DepartmentCode = "05", CityCode = "05631", IsActive = true,
        });
        ctx.TransitOfficeMandateConfigs.Add(new TransitOfficeMandateConfigEntity
        {
            Id = Guid.NewGuid(),
            TransitOfficeId = Sabaneta,
            TemplateCode = MandatoTemplateResolver.Auto,
            RequiresForNaturalPerson = false,
            MandataryFamily = "individuo",
            CustomTemplateKind = "none",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync(Ct);

        var config = await new MandateRequirementPolicy(ctx).ResolveAsync("5631000", CompanyA, Ct);

        config!.TemplateCode.Should().Be("sabaneta");
    }

    [Fact]
    public async Task ResolveByOfficeIdAsync_CodigoGuardadoQueNoCoteja_IgualResuelveLaConfig()
    {
        // HU #11704 — el bug de Matrícula Inicial: el trámite guardaba un código DIVIPOLA de 5 dígitos
        // ("25286") mientras el catálogo tiene el RUNT de 7 ("25286000"). Buscando por código no se
        // encontraba NADA y el mandato salía genérico y sin organismo resuelto; por id sí aparece.
        var funza = Guid.NewGuid();
        await using var ctx = NewContext();
        ctx.TransitOffices.Add(new TransitOffice
        {
            Id = funza, Code = "25286000", Name = "FUNZA",
            DepartmentCode = "25", CityCode = "25286", IsActive = true,
        });
        ctx.TransitOfficeMandateConfigs.Add(new TransitOfficeMandateConfigEntity
        {
            Id = Guid.NewGuid(),
            TransitOfficeId = funza,
            TemplateCode = MandatoTemplateResolver.Municipio,
            RequiresForNaturalPerson = true,
            MandataryFamily = "individuo",
            ChamberCity = "Funza",
            CustomTemplateKind = "none",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync(Ct);

        var policy = new MandateRequirementPolicy(ctx);

        // Por el código que quedó guardado en el trámite: nada.
        (await policy.ResolveAsync("25286", CompanyA, Ct)).Should().BeNull();

        // Por id: la parametrización real del organismo.
        var porId = await policy.ResolveByOfficeIdAsync(funza, CompanyA, Ct);
        porId.Should().NotBeNull();
        porId!.TemplateCode.Should().Be(MandatoTemplateResolver.Municipio);
        porId.TransitOfficeId.Should().Be(funza);
        porId.ChamberCity.Should().Be("Funza");
    }

    [Fact]
    public async Task ResolveByOfficeIdAsync_OrganismoInexistente_DevuelveNull()
    {
        await using var ctx = NewContext();
        (await new MandateRequirementPolicy(ctx).ResolveByOfficeIdAsync(Guid.NewGuid(), CompanyA, Ct))
            .Should().BeNull();
    }

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-mandate-config-{Guid.NewGuid()}")
            .Options);
}
