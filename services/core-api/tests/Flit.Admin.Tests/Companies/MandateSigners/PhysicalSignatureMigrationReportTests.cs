using Flit.Admin.Application.Companies.MandateSigners.PhysicalSignatureMigration;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// HU #13131 (ADR-0061) — reporte de migración de la firma física: mandatarios activos con
/// <c>signs_physically</c> sin firma del baúl y sin validación biométrica aprobada y vigente, con compañía,
/// organismo, forma de firma actual y dato faltante. Sin documento ni correo.
/// </summary>
public sealed class PhysicalSignatureMigrationReportTests
{
    private static readonly Guid Ot1 = Guid.NewGuid();
    private static readonly Guid Ot2 = Guid.NewGuid();
    private static readonly Guid CompanyA = Guid.NewGuid();
    private static readonly Guid CompanyB = Guid.NewGuid();
    private const string DocSolo = "1111111111";
    private const string DocBaul = "2222222222";
    private const string DocBio = "3333333333";
    private const string DocBioSinValidacion = "4444444444";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-physical-report-{Guid.NewGuid()}")
            .Options);

    private static async Task<FlitDbContext> SeedAsync(bool conMandatariosExtra = false)
    {
        var ct = TestContext.Current.CancellationToken;
        var ctx = NewContext();
        ctx.TransitOffices.AddRange(
            new TransitOffice { Id = Ot1, Code = "OT0000001", Name = "Bogotá", DepartmentCode = "11", CityCode = "11001", IsActive = true },
            new TransitOffice { Id = Ot2, Code = "OT0000002", Name = "Medellín", DepartmentCode = "05", CityCode = "05001", IsActive = true });
        ctx.Tenants.AddRange(Tenant(CompanyA, "Compañía A"), Tenant(CompanyB, "Compañía B"));

        AddSigner(ctx, "Solo Fisica", DocSolo, Ot1, CompanyA);
        AddSigner(ctx, "Con Baul", DocBaul, Ot1, CompanyA);
        AddSigner(ctx, "Con Biometria", DocBio, Ot1, CompanyA);
        AddSigner(ctx, "Biometria Sin Validacion", DocBioSinValidacion, Ot2, CompanyB, method: "biometria");

        if (conMandatariosExtra)
        {
            AddSigner(ctx, "Inactivo", "5555555555", Ot1, CompanyA, isActive: false);
            AddSigner(ctx, "Eliminado", "6666666666", Ot1, CompanyA, deleted: true);
            AddSigner(ctx, "Juridica", "9000000001", Ot1, CompanyA, model: MandateSignerModels.Juridica);
            AddSigner(ctx, "Sin marca fisica", "7777777777", Ot1, CompanyA, physical: false);
        }

        await ctx.SaveChangesAsync(ct);
        return ctx;
    }

    private static Tenant Tenant(Guid id, string name) => new()
    {
        Id = id,
        Code = $"R13131-{Guid.NewGuid():N}"[..20],
        LegalName = name,
        TaxId = TestNit.Unique(),
        TenantType = "RENTING",
        IsGroupParent = false,
        IsActive = true,
        CreatedAt = Now,
    };

    private static void AddSigner(
        FlitDbContext ctx, string name, string doc, Guid office, Guid company, string? method = null,
        bool isActive = true, bool deleted = false, bool physical = true,
        string model = MandateSignerModels.Natural)
    {
        var id = Guid.NewGuid();
        ctx.MandateSigners.Add(new MandateSigner
        {
            Id = id,
            TransitOfficeId = office,
            FullName = name,
            DocumentType = model == MandateSignerModels.Juridica ? "NIT" : "CC",
            DocumentNumber = doc,
            Email = $"{name.Replace(' ', '.')}@privado.test",
            IntegrityHash = new string('a', 64),
            RegisteredAt = Now,
            IsActive = isActive,
            DeletedAt = deleted ? Now : null,
            CreatedAt = Now,
            SignerModel = model,
            SignatureMethod = method,
        });
        ctx.MandateSignerTransitOffices.Add(new MandateSignerTransitOffice
        {
            Id = Guid.NewGuid(),
            MandateSignerId = id,
            TransitOfficeId = office,
            IsActive = true,
            SignsPhysically = physical,
            CreatedAt = Now,
        });
        ctx.MandateSignerCompanies.Add(new MandateSignerCompany
        {
            Id = Guid.NewGuid(),
            MandateSignerId = id,
            TransitOfficeId = office,
            CompanyTenantId = company,
            IsActive = true,
            CreatedAt = Now,
        });
    }

    private static ProcedureInstanceBiometricValidation Validacion(string doc, int haceDias) => new()
    {
        Status = BiometricEstados.Aprobado,
        DocumentType = "CC",
        DocumentNumber = doc,
        ValidatedAt = Now.AddDays(-haceDias),
        ValidUntil = Now.AddDays(30 - haceDias),
    };

    /// <summary>Validaciones por documento (en cualquier tenant de compañía) y firmas del baúl por documento.</summary>
    private static DbPhysicalSignatureMigrationReader Reader(
        FlitDbContext ctx,
        IReadOnlyDictionary<string, ProcedureInstanceBiometricValidation>? validaciones = null,
        params string[] documentosConBaul)
    {
        var repo = Substitute.For<IProcedureInstanceRepository>();
        repo.ListLatestBiometricValidationsByPersonsAsync(
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyCollection<(string DocumentTypeNorm, string DocumentNumberNorm)>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var docs = call.ArgAt<IReadOnlyCollection<(string DocumentTypeNorm, string DocumentNumberNorm)>>(1);
                return (IReadOnlyList<ProcedureInstanceBiometricValidation>)
                [
                    .. docs
                        .Where(d => validaciones?.ContainsKey(d.DocumentNumberNorm) == true)
                        .Select(d => validaciones![d.DocumentNumberNorm]),
                ];
            });

        var vault = Substitute.For<ISignatureVaultPolicy>();
        vault.ResolveMandatarioAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => documentosConBaul.Contains(call.ArgAt<string>(2))
                ? new SignatureVaultMatch(Guid.NewGuid(), "x", "h", "p/x.png", "s", DateOnly.MinValue, DateOnly.MaxValue, call.ArgAt<string>(2), "c")
                : null);

        var otStatus = Substitute.For<ITransitOfficeOperationalStatusReader>();
        return new DbPhysicalSignatureMigrationReader(
            ctx, vault, otStatus, new IdentityVigenciaPorDocumentoResolver(repo));
    }

    [Fact]
    public async Task AC2_ListaSoloALosQueDependenDeLaFirmaFisica_ConCompaniaOrganismoFormaYDatoFaltante()
    {
        await using var ctx = await SeedAsync();
        var reader = Reader(
            ctx,
            new Dictionary<string, ProcedureInstanceBiometricValidation>
            {
                [DocBio] = Validacion(DocBio, 50), // HU #13130b: aprobada hace 50 días ya cuenta (sin renovación)
            },
            DocBaul);

        var rows = await reader.ListAsync(null, TestContext.Current.CancellationToken);

        rows.Select(r => r.FullName).Should().BeEquivalentTo(["Solo Fisica", "Biometria Sin Validacion"]);

        var solo = rows.Single(r => r.FullName == "Solo Fisica");
        solo.CompanyTenantId.Should().Be(CompanyA);
        solo.CompanyName.Should().Be("Compañía A");
        solo.TransitOfficeId.Should().Be(Ot1);
        solo.TransitOfficeName.Should().Be("Bogotá");
        solo.CurrentSignatureForm.Should().Be(PhysicalSignatureMigrationForms.FirmaFisica);
        solo.DeclaredSignatureMethod.Should().BeNull();
        solo.MissingData.Should().Be(PhysicalSignatureMigrationMissing.BaulOBiometria);

        var sinValidacion = rows.Single(r => r.FullName == "Biometria Sin Validacion");
        sinValidacion.DeclaredSignatureMethod.Should().Be("biometria");
        sinValidacion.MissingData.Should().Be(PhysicalSignatureMigrationMissing.ValidacionBiometrica);
    }

    [Fact]
    public async Task AC2_SeFiltraPorOrganismo()
    {
        await using var ctx = await SeedAsync();
        var reader = Reader(ctx);

        var soloOt2 = await reader.ListAsync(Ot2, TestContext.Current.CancellationToken);

        soloOt2.Should().OnlyContain(r => r.TransitOfficeId == Ot2);
        soloOt2.Select(r => r.FullName).Should().BeEquivalentTo(["Biometria Sin Validacion"]);
    }

    [Fact]
    public async Task AC3_UnMandatarioMigradoSaleDelReporte_PorBaulOPorBiometria()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();

        var antes = await Reader(ctx).ListAsync(null, ct);
        antes.Select(r => r.FullName).Should().Contain("Solo Fisica");

        // Se le vincula una firma del baúl.
        var conBaul = await Reader(ctx, null, DocSolo).ListAsync(null, ct);
        conBaul.Select(r => r.FullName).Should().NotContain("Solo Fisica");

        // Se le aprueba la validación biométrica.
        var conBio = await Reader(
                ctx,
                new Dictionary<string, ProcedureInstanceBiometricValidation> { [DocSolo] = Validacion(DocSolo, 1) })
            .ListAsync(null, ct);
        conBio.Select(r => r.FullName).Should().NotContain("Solo Fisica");
    }

    [Fact]
    public async Task ExcluyeInactivosEliminadosSinMarcaFisicaYModelosSinFirmaPersonal()
    {
        await using var ctx = await SeedAsync(conMandatariosExtra: true);

        var rows = await Reader(ctx).ListAsync(null, TestContext.Current.CancellationToken);

        rows.Select(r => r.FullName).Should()
            .NotContain(["Inactivo", "Eliminado", "Juridica", "Sin marca fisica"]);
    }

    [Fact]
    public async Task AC5_ElReporteEsSoloLectura_LasFilasConFirmaFisicaNoSeTocan()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();

        await Reader(ctx).ListAsync(null, ct);

        ctx.MandateSignerTransitOffices.Count(o => o.SignsPhysically).Should().Be(4);
    }

    [Fact]
    public async Task AC5_ElReporteNoIncluyeDocumentoNiCorreo_NiEnElCsv()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        var rows = await new GetPhysicalSignatureMigrationReportHandler(Reader(ctx)).HandleAsync(null, ct);

        typeof(PhysicalSignatureMigrationRow).GetProperties().Select(p => p.Name)
            .Should().NotContain(n => n.Contains("Document", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Email", StringComparison.OrdinalIgnoreCase));

        var csv = PhysicalSignatureMigrationCsv.Build(rows);
        csv.Should().NotContain(DocSolo).And.NotContain(DocBioSinValidacion).And.NotContain("@privado.test");
        csv.Should().StartWith("mandatario_id,mandatario,compania_id,compania,organismo_id");
        csv.Should().Contain("Solo Fisica").And.Contain("firma_fisica").And.Contain("firma_baul_o_validacion_biometrica");
    }

    [Fact]
    public void Csv_NeutralizaFormulasYEscapaComillas()
    {
        PhysicalSignatureMigrationCsv.Cell("=HYPERLINK(\"x\")").Should().Be("\"'=HYPERLINK(\"\"x\"\")\"");
        PhysicalSignatureMigrationCsv.Cell("Ana \"la\" Mandataria").Should().Be("\"Ana \"\"la\"\" Mandataria\"");
        PhysicalSignatureMigrationCsv.Cell(null).Should().BeEmpty();
    }

    [Fact]
    public async Task Handler_OrdenaPorOrganismoCompaniaYNombre()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();

        var rows = await new GetPhysicalSignatureMigrationReportHandler(Reader(ctx)).HandleAsync(null, ct);

        rows.Select(r => r.TransitOfficeName).Should().BeInAscendingOrder();
    }
}
