using Flit.Admin.Application.Companies.MandateSigners.IdentityValidation;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// HU #13247 (Feature #13245, Épica #13090) — reporte de mandatarios afectados: Persona natural activa con forma de firma
/// biometría (o legado sin forma ni baúl) SIN validación propia aprobada, con compañía, organismo y correo para avisarles. La
/// aprobación de otro rol con el mismo documento no los saca del reporte (sin backfill). Solo Super Admin (lo impone el
/// endpoint). <para>Uso: <c>reader.ListAsync(null)</c> lista una fila por vínculo con compañía.</para>
/// </summary>
public sealed class MandateIdentityAffectedReportTests
{
    private static readonly Guid Ot1 = Guid.NewGuid();
    private static readonly Guid Ot2 = Guid.NewGuid();
    private static readonly Guid CompanyA = Guid.NewGuid();
    private static readonly Guid CompanyB = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid Aprobado = Guid.NewGuid();
    private static readonly Guid SinValidacion = Guid.NewGuid();
    private static readonly Guid EnCurso = Guid.NewGuid();
    private static readonly Guid SoloOtroRol = Guid.NewGuid();

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-identity-affected-{Guid.NewGuid()}")
            .Options);

    private static async Task<FlitDbContext> SeedAsync()
    {
        var ctx = NewContext();
        ctx.TransitOffices.AddRange(
            new TransitOffice { Id = Ot1, Code = "OT0000001", Name = "Bogotá", DepartmentCode = "11", CityCode = "11001", IsActive = true },
            new TransitOffice { Id = Ot2, Code = "OT0000002", Name = "Medellín", DepartmentCode = "05", CityCode = "05001", IsActive = true });
        ctx.Tenants.AddRange(Tenant(CompanyA, "Compañía A"), Tenant(CompanyB, "Compañía B"));

        Signer(ctx, Aprobado, "Aprobado Propio", "1000000001", Ot1, CompanyA, "biometria");
        Signer(ctx, SinValidacion, "Sin Validacion", "1000000002", Ot1, CompanyA, "biometria");
        Signer(ctx, EnCurso, "En Curso", "1000000003", Ot2, CompanyB, null); // legado sin forma ni baúl = biometría efectiva
        Signer(ctx, SoloOtroRol, "Solo Otro Rol", "1000000004", Ot2, CompanyB, "biometria");
        Signer(ctx, Guid.NewGuid(), "Con Baul", "1000000005", Ot1, CompanyA, "baul");
        Signer(ctx, Guid.NewGuid(), "Inactivo", "1000000006", Ot1, CompanyA, "biometria", isActive: false);
        Signer(ctx, Guid.NewGuid(), "Eliminado", "1000000007", Ot1, CompanyA, "biometria", deleted: true);
        Signer(ctx, Guid.NewGuid(), "Juridica", "9000000001", Ot1, CompanyA, null, model: MandateSignerModels.Juridica);

        await ctx.SaveChangesAsync(Ct);
        return ctx;
    }

    private static Tenant Tenant(Guid id, string name) => new()
    {
        Id = id,
        Code = $"R13247-{Guid.NewGuid():N}"[..20],
        LegalName = name,
        TaxId = TestNit.Unique(),
        TenantType = "RENTING",
        IsGroupParent = false,
        IsActive = true,
        CreatedAt = Now,
    };

    private static void Signer(
        FlitDbContext ctx, Guid id, string name, string doc, Guid office, Guid company, string? method,
        bool isActive = true, bool deleted = false, string model = MandateSignerModels.Natural)
    {
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

    private static ProcedureInstanceBiometricValidation Propia(Guid signer, string doc, string estado) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = CompanyA,
        PartyRole = BiometricRules.ParteMandatario,
        MandateSignerId = signer,
        DocumentType = "CC",
        DocumentNumber = doc,
        Status = estado,
        CreatedAt = Now.AddDays(-5),
        ValidatedAt = estado == BiometricEstados.Aprobado ? Now.AddDays(-5) : null,
    };

    private static DbMandateIdentityAffectedReader Reader(FlitDbContext ctx)
    {
        // Solo las validaciones PROPIAS llegan al resolver: la aprobación de otro rol con el mismo documento no figura.
        var repo = Substitute.For<IProcedureInstanceRepository>();
        repo.ListMandatarioValidationsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<ProcedureInstanceBiometricValidation>)
            [
                Propia(Aprobado, "1000000001", BiometricEstados.Aprobado),
                Propia(EnCurso, "1000000003", BiometricEstados.EnProceso),
            ]);
        return new DbMandateIdentityAffectedReader(ctx, new IdentityVigenciaPorDocumentoResolver(repo));
    }

    [Fact]
    public async Task ListaSoloALosMandatariosConBiometriaSinValidacionPropiaAprobada_ConCompaniaOrganismoYCorreo()
    {
        await using var ctx = await SeedAsync();

        var rows = await Reader(ctx).ListAsync(null, Ct);

        rows.Select(r => r.FullName).Should().BeEquivalentTo(["Sin Validacion", "En Curso", "Solo Otro Rol"]);

        var sin = rows.Single(r => r.FullName == "Sin Validacion");
        sin.CompanyTenantId.Should().Be(CompanyA);
        sin.CompanyName.Should().Be("Compañía A");
        sin.TransitOfficeId.Should().Be(Ot1);
        sin.TransitOfficeCode.Should().Be("OT0000001");
        sin.TransitOfficeName.Should().Be("Bogotá");
        sin.Email.Should().Be("Sin.Validacion@privado.test");
        sin.IdentityStatus.Should().Be("none");

        rows.Single(r => r.FullName == "En Curso").IdentityStatus.Should().Be("pending");
        // Tenía aprobado un comprador con su misma cédula (no llega al resolver): sigue afectado, sin backfill.
        rows.Single(r => r.FullName == "Solo Otro Rol").IdentityStatus.Should().Be("none");
    }

    [Fact]
    public async Task SeFiltraPorOrganismo()
    {
        await using var ctx = await SeedAsync();

        var rows = await Reader(ctx).ListAsync(Ot2, Ct);

        rows.Should().OnlyContain(r => r.TransitOfficeId == Ot2);
        rows.Select(r => r.FullName).Should().BeEquivalentTo(["En Curso", "Solo Otro Rol"]);
    }

    [Fact]
    public async Task ElHandlerOrdenaPorOrganismoCompaniaYNombre_YElCsvIncluyeElCorreoNeutralizandoFormulas()
    {
        var rows = new List<MandateIdentityAffectedRow>
        {
            new(Guid.NewGuid(), "Zoe", "zoe@x.test", CompanyA, "Compañía A", Ot2, "OT2", "Medellín", "none"),
            new(Guid.NewGuid(), "=Ana", "ana@x.test", CompanyA, "Compañía A", Ot1, "OT1", "Bogotá", "pending"),
        };
        var reader = Substitute.For<IMandateIdentityAffectedReader>();
        reader.ListAsync(null, Arg.Any<CancellationToken>()).Returns(rows);

        var ordered = await new GetMandateIdentityAffectedReportHandler(reader).HandleAsync(null, Ct);
        var csv = MandateIdentityAffectedCsv.Build(ordered);

        ordered.Select(r => r.TransitOfficeName).Should().Equal("Bogotá", "Medellín");
        csv.Split("\r\n")[0].Should().Contain("correo").And.Contain("estado_identidad");
        csv.Should().Contain("\"ana@x.test\"").And.Contain("\"'=Ana\"");
    }
}
