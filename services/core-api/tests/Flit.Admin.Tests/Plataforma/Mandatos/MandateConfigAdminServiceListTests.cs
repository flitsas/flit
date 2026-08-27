using Flit.Admin.Application.Plataforma.Mandatos;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Tests.TestDoubles;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Tramites.Application.Ocr;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Plataforma.Mandatos;

/// <summary>
/// Listado SuperAdmin de mandatos: solo OT activos en FLIT (tenant OT + is_active).
/// </summary>
public sealed class MandateConfigAdminServiceListTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static readonly Guid ActiveId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid InactiveId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid NoTenantId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid CompanyId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    [Fact]
    public async Task ListAsync_SoloIncluyeOtConTenantActivo()
    {
        await using var db = NewDb();
        var catalog = Substitute.For<ITransitOfficeCatalog>();
        catalog.All.Returns(
        [
            new TransitOfficeEntry(ActiveId, "5631000", "Sabaneta", "05", "05631"),
            new TransitOfficeEntry(InactiveId, "5088000", "Bello", "05", "05088"),
            new TransitOfficeEntry(NoTenantId, "5266000", "Envigado", "05", "05266"),
        ]);

        var status = new StubTransitOfficeOperationalStatusReader()
            .Set(ActiveId, hasTenant: true, estadoActivo: true)
            .Set(InactiveId, hasTenant: true, estadoActivo: false)
            .Set(NoTenantId, hasTenant: false, estadoActivo: null);

        var service = new MandateConfigAdminService(
            db,
            catalog,
            status,
            Substitute.For<IDocumentOcrAnalyzer>(),
            Substitute.For<IMandateTemplateStorage>());

        var items = await service.ListAsync(Ct);

        items.Should().ContainSingle();
        items[0].OfficeId.Should().Be(ActiveId);
        items[0].Name.Should().Be("Sabaneta");
    }

    [Fact]
    public async Task ListCompanyRules_WithoutExplicitRule_InheritsOtAssignmentMode()
    {
        await using var db = NewDb();
        db.Tenants.Add(new Tenant
        {
            Id = CompanyId,
            Code = "CIA-1",
            LegalName = "Gestora de Prueba S.A.S.",
            TaxId = "900123456",
            TenantType = "company",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
        {
            Id = Guid.NewGuid(),
            TenantId = CompanyId,
            TransitOfficeId = ActiveId,
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.TransitOfficeMandateConfigs.Add(new TransitOfficeMandateConfigEntity
        {
            Id = Guid.NewGuid(),
            TransitOfficeId = ActiveId,
            TemplateCode = "generico",
            AssignmentMode = "open",
            MandataryFamily = "individuo",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(Ct);

        var catalog = Substitute.For<ITransitOfficeCatalog>();
        catalog.GetById(ActiveId).Returns(new TransitOfficeEntry(ActiveId, "11001000", "Bogotá", "11", "11001"));

        var service = new MandateConfigAdminService(
            db,
            catalog,
            new StubTransitOfficeOperationalStatusReader().Set(ActiveId, hasTenant: true, estadoActivo: true),
            Substitute.For<IDocumentOcrAnalyzer>(),
            Substitute.For<IMandateTemplateStorage>());

        var items = await service.ListCompanyRulesAsync(ActiveId, Ct);

        items.Should().ContainSingle();
        items[0].CompanyTenantId.Should().Be(CompanyId);
        items[0].HasExplicitRule.Should().BeFalse();
        items[0].AssignmentMode.Should().Be("open");
        items[0].CompanyTaxId.Should().Be("900123456");
        items[0].CompanyCode.Should().Be("CIA-1");
        items[0].DefaultMandateSignerName.Should().BeNull();
    }

    [Fact]
    public async Task ListCompanyRules_WithDefaultSigner_IncludesPersonSnapshot()
    {
        await using var db = NewDb();
        var signerId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        db.Tenants.Add(new Tenant
        {
            Id = CompanyId,
            Code = "CIA-1",
            LegalName = "Gestora de Prueba S.A.S.",
            TaxId = "900123456",
            TenantType = "company",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
        {
            Id = Guid.NewGuid(),
            TenantId = CompanyId,
            TransitOfficeId = ActiveId,
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.MandateSigners.Add(new MandateSigner
        {
            Id = signerId,
            TransitOfficeId = ActiveId,
            FullName = "Carlos Pérez",
            DocumentType = "CC",
            DocumentNumber = "1020304050",
            IntegrityHash = new string('a', 64),
            RegisteredAt = DateTimeOffset.UtcNow,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.CompanyOtMandateRules.Add(new CompanyOtMandateRuleEntity
        {
            Id = Guid.NewGuid(),
            CompanyTenantId = CompanyId,
            TransitOfficeId = ActiveId,
            AssignmentMode = "signer",
            MandataryFamily = "individuo",
            DefaultMandateSignerId = signerId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(Ct);

        var catalog = Substitute.For<ITransitOfficeCatalog>();
        catalog.GetById(ActiveId).Returns(new TransitOfficeEntry(ActiveId, "11001000", "Bogotá", "11", "11001"));

        var service = new MandateConfigAdminService(
            db,
            catalog,
            new StubTransitOfficeOperationalStatusReader().Set(ActiveId, hasTenant: true, estadoActivo: true),
            Substitute.For<IDocumentOcrAnalyzer>(),
            Substitute.For<IMandateTemplateStorage>());

        var items = await service.ListCompanyRulesAsync(ActiveId, Ct);

        items.Should().ContainSingle();
        items[0].DefaultMandateSignerId.Should().Be(signerId);
        items[0].DefaultMandateSignerName.Should().Be("Carlos Pérez");
        items[0].DefaultMandateSignerDocumentType.Should().Be("CC");
        items[0].DefaultMandateSignerDocumentNumber.Should().Be("1020304050");
        items[0].DefaultMandateSignerIntegrityHash.Should().HaveLength(64);
        items[0].CompanyTaxId.Should().Be("900123456");
        items[0].CompanyCode.Should().Be("CIA-1");
    }

    [Fact]
    public async Task GetAsync_IncludesDefaultSignerName()
    {
        await using var db = NewDb();
        var signerId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        db.MandateSigners.Add(new MandateSigner
        {
            Id = signerId,
            TransitOfficeId = ActiveId,
            FullName = "Ana Restrepo",
            DocumentType = "CC",
            DocumentNumber = "111",
            IntegrityHash = new string('b', 64),
            RegisteredAt = DateTimeOffset.UtcNow,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.TransitOfficeMandateConfigs.Add(new TransitOfficeMandateConfigEntity
        {
            Id = Guid.NewGuid(),
            TransitOfficeId = ActiveId,
            TemplateCode = "generico",
            AssignmentMode = "signer",
            MandataryFamily = "individuo",
            DefaultMandateSignerId = signerId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(Ct);

        var catalog = Substitute.For<ITransitOfficeCatalog>();
        catalog.GetById(ActiveId).Returns(new TransitOfficeEntry(ActiveId, "11001000", "Bogotá", "11", "11001"));

        var service = new MandateConfigAdminService(
            db,
            catalog,
            new StubTransitOfficeOperationalStatusReader().Set(ActiveId, hasTenant: true, estadoActivo: true),
            Substitute.For<IDocumentOcrAnalyzer>(),
            Substitute.For<IMandateTemplateStorage>());

        var view = await service.GetAsync(ActiveId, Ct);

        view!.DefaultMandateSignerId.Should().Be(signerId);
        view.DefaultMandateSignerName.Should().Be("Ana Restrepo");
        view.DefaultMandateSignerDocumentType.Should().Be("CC");
        view.DefaultMandateSignerDocumentNumber.Should().Be("111");
        view.DefaultMandateSignerIntegrityHash.Should().HaveLength(64);
    }

    private static FlitDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"mandate-list-{Guid.NewGuid()}")
            .Options);
}
