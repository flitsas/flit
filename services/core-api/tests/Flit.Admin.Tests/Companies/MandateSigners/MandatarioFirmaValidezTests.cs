using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Domain.Identity;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Queries.Domain.Time;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// HU #13130 (ADR-0061) — la Persona natural firma solo con su vigencia propia Y su validación biométrica
/// vigentes (regla de 30 días del módulo Identidad). Las dos vigencias conviven.
/// </summary>
public sealed class MandatarioFirmaValidezTests
{
    private static readonly Guid Ot = Guid.NewGuid();
    private static readonly Guid Gestora = Guid.NewGuid();
    private static readonly Guid Signer = Guid.NewGuid();
    private const string Documento = "1020304050";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static DateOnly Hoy => ColombiaTime.Today(TimeProvider.System);

    // ---- Regla pura (ficha admin y directorio comparten esta evaluación) ----

    [Theory]
    [InlineData(MandateValidityStatus.Vigente, AdminIdentityVigencia.Valid, true, null)]
    [InlineData(MandateValidityStatus.PorVencer, AdminIdentityVigencia.Valid, true, null)]
    [InlineData(MandateValidityStatus.Vigente, AdminIdentityVigencia.Expired, false, MandateSignerFirmaValidez.MotivoSinValidacionAprobada)]
    [InlineData(MandateValidityStatus.Vencido, AdminIdentityVigencia.Valid, false, MandateSignerFirmaValidez.MotivoFueraDeVigencia)]
    [InlineData(MandateValidityStatus.NoVigente, AdminIdentityVigencia.Valid, false, MandateSignerFirmaValidez.MotivoFueraDeVigencia)]
    [InlineData(MandateValidityStatus.Vigente, AdminIdentityVigencia.None, false, MandateSignerFirmaValidez.MotivoSinValidacionAprobada)]
    [InlineData(MandateValidityStatus.Vigente, AdminIdentityVigencia.Pending, false, MandateSignerFirmaValidez.MotivoSinValidacionAprobada)]
    [InlineData(MandateValidityStatus.Vencido, AdminIdentityVigencia.Expired, false, MandateSignerFirmaValidez.MotivoFueraDeVigencia)]
    [InlineData(MandateValidityStatus.Inactivo, AdminIdentityVigencia.Valid, false, MandateSignerFirmaValidez.MotivoInactivo)]
    public void Biometria_ExigeVigenciaPropiaYUnaAprobacion(string vigencia, string identidad, bool valida, string? motivo)
    {
        var r = MandateSignerFirmaValidez.Evaluar(
            MandateSignerModels.Natural, MandateSignatureMethods.Biometria, vigencia, identidad, false);

        r.Should().NotBeNull();
        r!.Value.Valida.Should().Be(valida);
        r.Value.Motivo.Should().Be(motivo);
    }

    [Fact]
    public void Baul_SoloExigeLaVigenciaPropia_YNoMiraLaBiometria()
    {
        MandateSignerFirmaValidez.Evaluar("natural", "baul", MandateValidityStatus.Vigente, AdminIdentityVigencia.None, true)!
            .Value.Valida.Should().BeTrue();
        MandateSignerFirmaValidez.Evaluar("natural", "baul", MandateValidityStatus.Vencido, AdminIdentityVigencia.Valid, true)!
            .Value.Motivo.Should().Be(MandateSignerFirmaValidez.MotivoFueraDeVigencia);
    }

    [Fact]
    public void LegadoSinFormaDeFirma_SeInfiereDelBaul_OSeExigeBiometria()
    {
        MandateSignerFirmaValidez.Evaluar("natural", null, MandateValidityStatus.Vigente, AdminIdentityVigencia.None, true)!
            .Value.Valida.Should().BeTrue();
        MandateSignerFirmaValidez.Evaluar("natural", null, MandateValidityStatus.Vigente, AdminIdentityVigencia.Expired, false)!
            .Value.Motivo.Should().Be(MandateSignerFirmaValidez.MotivoSinValidacionAprobada);
    }

    [Theory]
    [InlineData(MandateSignerModels.Juridica)]
    [InlineData(MandateSignerModels.FormatoBlanco)]
    public void PersonaJuridicaYFormatoEnBlanco_NoTienenFirmaPersonalQueValidar(string modelo) =>
        MandateSignerFirmaValidez.Evaluar(modelo, null, MandateValidityStatus.Vigente, AdminIdentityVigencia.None, false)
            .Should().BeNull();

    [Fact]
    public void Ficha_ItemCalculaLaFirmaValidaConSuVigenciaYSuIdentidad()
    {
        var item = new MandateSignerItem
        {
            SignerModel = "natural",
            SignatureMethod = "biometria",
            ValidityKind = "range",
            ValidFrom = Hoy.AddDays(-20),
            ValidTo = Hoy.AddDays(3),
            IsActive = true,
            IdentityStatus = AdminIdentityVigencia.Expired,
        };

        item.ValidityStatusOn(Hoy).Should().Be(MandateValidityStatus.PorVencer);
        item.FirmaValidezOn(Hoy)!.Value.Motivo.Should().Be(MandateSignerFirmaValidez.MotivoSinValidacionAprobada);

        var aprobado = new MandateSignerItem
        {
            SignerModel = "natural",
            SignatureMethod = "biometria",
            ValidityKind = "range",
            ValidFrom = Hoy.AddDays(-20),
            ValidTo = Hoy.AddDays(3),
            IsActive = true,
            IdentityStatus = AdminIdentityVigencia.Valid,
        };
        aprobado.FirmaValidezOn(Hoy)!.Value.Valida.Should().BeTrue();
    }

    // ---- Directorio de trámites: el candidato lleva FirmaValida y el motivo ----

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-mandate-firma-validez-{Guid.NewGuid()}")
            .Options);

    private static async Task<FlitDbContext> SeedAsync(
        string metodo, string validityKind, DateOnly? desde, DateOnly? hasta)
    {
        var ctx = NewContext();
        ctx.MandateSigners.Add(new MandateSigner
        {
            Id = Signer,
            TransitOfficeId = Ot,
            FullName = "Ana Restrepo",
            DocumentType = "CC",
            DocumentNumber = Documento,
            IntegrityHash = new string('a', 64),
            RegisteredAt = Now,
            IsActive = true,
            CreatedAt = Now,
            SignerModel = "natural",
            SignatureMethod = metodo,
            ValidityKind = validityKind,
            ValidFrom = desde,
            ValidTo = hasta,
        });
        ctx.MandateSignerCompanies.Add(new MandateSignerCompany
        {
            Id = Guid.NewGuid(),
            MandateSignerId = Signer,
            TransitOfficeId = Ot,
            CompanyTenantId = Gestora,
            IsActive = true,
            CreatedAt = Now,
        });
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return ctx;
    }

    private static MandateSignerDirectory Directorio(FlitDbContext ctx, ProcedureInstanceBiometricValidation? latest)
    {
        IReadOnlyList<ProcedureInstanceBiometricValidation> rows = latest is null ? [] : [latest];
        var repo = Substitute.For<IProcedureInstanceRepository>();
        repo.ListBiometricValidationsByPersonAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns((Array.Empty<ProcedureInstanceBiometricValidation>(), 0, false));
        repo.ListBiometricValidationsByPersonAsync(Gestora, "CC", Documento, 0, 1, Arg.Any<CancellationToken>())
            .Returns((rows, rows.Count, false));
        var reader = Substitute.For<ITransitOfficeOperationalStatusReader>();
        reader.GetByIdAsync(Ot, Arg.Any<CancellationToken>()).Returns((TransitOfficeOperationalStatusItem?)null);
        return new MandateSignerDirectory(ctx, reader, new IdentityVigenciaPorDocumentoResolver(repo));
    }

    private static ProcedureInstanceBiometricValidation Aprobada(int haceDias) => new()
    {
        Status = BiometricEstados.Aprobado,
        DocumentType = "CC",
        DocumentNumber = Documento,
        ValidatedAt = Now.AddDays(-haceDias),
        ValidUntil = Now.AddDays(30 - haceDias),
        CertificateHash = "hash-mandatario",
    };

    [Fact]
    public async Task AC1_VigenciaFijaYBiometriaReciente_FirmaValida()
    {
        await using var ctx = await SeedAsync("biometria", "fixed", null, null);

        var c = (await Directorio(ctx, Aprobada(5)).GetCandidatesAsync(Ot, Gestora, null, TestContext.Current.CancellationToken))
            .Should().ContainSingle().Subject;

        c.FirmaValida.Should().BeTrue();
        c.MotivoSinFirma.Should().BeNull();
        c.IdentityVigente.Should().BeTrue();
        c.CertificadoIdentidad.Should().Be("hash-mandatario");
    }

    [Fact]
    public async Task AC2_RangoVigenteConBiometriaVigente_FirmaValida()
    {
        await using var ctx = await SeedAsync("biometria", "range", Hoy.AddDays(-10), Hoy.AddDays(5));

        var c = (await Directorio(ctx, Aprobada(1)).GetCandidatesAsync(Ot, Gestora, null, TestContext.Current.CancellationToken))
            .Should().ContainSingle().Subject;

        c.FirmaValida.Should().BeTrue();
    }

    [Fact]
    public async Task AC3_VigenciaPropiaActivaYBiometriaAprobadaHace40Dias_FirmaValidaSinRenovacion()
    {
        // HU #13130b (decisión del PO, 01-oct): el mandatario no renueva su identidad.
        await using var ctx = await SeedAsync("biometria", "fixed", null, null);

        var c = (await Directorio(ctx, Aprobada(40)).GetCandidatesAsync(Ot, Gestora, null, TestContext.Current.CancellationToken))
            .Should().ContainSingle().Subject;

        c.FirmaValida.Should().BeTrue();
        c.MotivoSinFirma.Should().BeNull();
        c.IdentityVigente.Should().BeTrue();
        c.CertificadoIdentidad.Should().Be("hash-mandatario");
    }

    [Fact]
    public async Task AC3b_BiometriaAprobadaHace40DiasPeroVigenciaPropiaVencida_FueraDeVigencia()
    {
        await using var ctx = await SeedAsync("biometria", "range", Hoy.AddDays(-60), Hoy.AddDays(-1));

        var c = (await Directorio(ctx, Aprobada(40)).GetCandidatesAsync(Ot, Gestora, null, TestContext.Current.CancellationToken))
            .Should().ContainSingle().Subject;

        c.FirmaValida.Should().BeFalse();
        c.MotivoSinFirma.Should().Be(MandateSignerFirmaValidez.MotivoFueraDeVigencia);
    }

    [Fact]
    public async Task AC4_RangoTerminadoConBiometriaVigente_SinFirmaConMotivoFueraDeVigencia()
    {
        await using var ctx = await SeedAsync("biometria", "range", Hoy.AddDays(-30), Hoy.AddDays(-1));

        var c = (await Directorio(ctx, Aprobada(2)).GetCandidatesAsync(Ot, Gestora, null, TestContext.Current.CancellationToken))
            .Should().ContainSingle().Subject;

        c.FirmaValida.Should().BeFalse();
        c.MotivoSinFirma.Should().Be(MandateSignerFirmaValidez.MotivoFueraDeVigencia);
        c.IdentityVigente.Should().BeTrue("la identidad sigue vigente: son dos vigencias independientes");
    }

    [Fact]
    public async Task AC5_BiometriaSinNingunaValidacionAprobada_SinFirmaAunqueLaVigenciaEstaActiva()
    {
        await using var ctx = await SeedAsync("biometria", "fixed", null, null);

        var c = (await Directorio(ctx, null).GetCandidatesAsync(Ot, Gestora, null, TestContext.Current.CancellationToken))
            .Should().ContainSingle().Subject;

        c.FirmaValida.Should().BeFalse();
        c.MotivoSinFirma.Should().Be(MandateSignerFirmaValidez.MotivoSinValidacionAprobada);
    }

    [Fact]
    public async Task GetById_PropagaFirmaValidaYMotivo()
    {
        await using var ctx = await SeedAsync("biometria", "range", Hoy.AddDays(-30), Hoy.AddDays(-1));

        var c = await Directorio(ctx, Aprobada(2)).GetByIdAsync(Signer, TestContext.Current.CancellationToken);

        c!.FirmaValida.Should().BeFalse();
        c.MotivoSinFirma.Should().Be(MandateSignerFirmaValidez.MotivoFueraDeVigencia);
    }

    [Fact]
    public async Task Baul_ConRangoTerminado_SinFirmaAunqueNoDependeDeLaBiometria()
    {
        await using var ctx = await SeedAsync("baul", "range", Hoy.AddDays(-30), Hoy.AddDays(-1));

        var c = (await Directorio(ctx, null).GetCandidatesAsync(Ot, Gestora, null, TestContext.Current.CancellationToken))
            .Should().ContainSingle().Subject;

        c.FirmaValida.Should().BeFalse();
        c.MotivoSinFirma.Should().Be(MandateSignerFirmaValidez.MotivoFueraDeVigencia);
    }

    [Fact]
    public void AC6_LaReglaDeTreintaDiasDelModuloIdentidadNoCambia()
    {
        BiometricRules.VigenciaDias.Should().Be(30);

        BiometricRules.EsAprobadaVigente(Aprobada(29), Now).Should().BeTrue();
        BiometricRules.EsAprobadaVigente(Aprobada(31), Now).Should().BeFalse(
            "el trámite sigue exigiendo la ventana de 30 días; solo el mandatario queda exento (HU #13130b)");
        IdentityVigenciaClassifier.Classify(Aprobada(40), Now).Should().Be(IdentityVigenciaEstados.Vencida);
    }
}
