using Flit.Admin.Application.Companies.MandateSigners;
using Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// HU #13123 (Epic #13090, F1) — el alta desde el OT reutiliza las validaciones de la compañía
/// (<c>ValidateSigningMeans</c>): firma del baúl contra el tenant de la compañía, exclusividad y medio de
/// firma (el correo no cuenta). <para>Uso de ejemplo: <c>handler.HandleAsync(command con
/// ValidateSigningMeans = true, SignatureVaultId = firma)</c> devuelve <c>IsValid == true</c> si la firma es
/// de esa persona, activa y vigente.</para>
/// </summary>
public sealed class CreateMandateSignerOtSigningMeansTests
{
    private static readonly Guid Office = MandateSignerHandlerTests.Office;
    private static readonly Guid CompanyA = MandateSignerHandlerTests.CompanyA;
    private static readonly Guid CompanyB = MandateSignerHandlerTests.CompanyB;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CreateMandateSignerHandler Handler(FlitDbContext ctx) =>
        new(
            new DbTransitOfficeOperationalStatusReader(ctx),
            new DbMandateSignerReader(ctx),
            new MandateSignerRepository(ctx),
            new DbSignatureVaultReader(ctx),
            identityLauncher: new StubMandateSignerIdentityLauncher());

    private static async Task<Guid> SeedFirmaAsync(
        FlitDbContext ctx, Guid tenant, string documento, string estado = "activa")
    {
        var id = Guid.NewGuid();
        var hoy = DateOnly.FromDateTime(DateTimeOffset.UtcNow.AddHours(-5).Date);
        ctx.SignatureVault.Add(new SignatureVaultEntity
        {
            Id = id,
            TenantId = tenant,
            DocumentType = "CC",
            DocumentNumber = documento,
            FullName = "Ana Restrepo",
            SignatureHash = "sha",
            StoragePath = "vault/f.png",
            StorageSha256 = "sha",
            Estado = estado,
            VigenciaDesde = hoy.AddDays(-1),
            VigenciaHasta = hoy.AddYears(1),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync(Ct);
        return id;
    }

    private static async Task SeedBiometriaAsync(
        FlitDbContext ctx, Guid tenant, string documento, string estado, int diasDesdeAprobacion = 1)
    {
        var now = DateTimeOffset.UtcNow;
        var aprobada = estado == BiometricEstados.Aprobado;
        ctx.ProcedureInstanceBiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            DocumentType = "CC",
            DocumentNumber = documento,
            Status = estado,
            Provider = BiometricProviders.Kyverum,
            TokenHash = "hash",
            ExpiresAt = now.AddHours(1),
            ValidatedAt = aprobada ? now.AddDays(-diasDesdeAprobacion) : null,
            ValidUntil = aprobada ? now.AddDays(30 - diasDesdeAprobacion) : null,
            CreatedAt = now.AddDays(-diasDesdeAprobacion),
        });
        await ctx.SaveChangesAsync(Ct);
    }

    private static CreateMandateSignerCommand Alta(
        string documento, Guid company, Guid? firma, bool validar = true, string? metodo = "baul") =>
        new()
        {
            TransitOfficeId = Office,
            FullName = "Ana Restrepo",
            DocumentNumber = documento,
            CompanyTenantIds = [company],
            DocumentType = "CC",
            Email = "ana@flit.test",
            TransitOfficeIds = [Office],
            SignatureVaultId = firma,
            SignatureMethod = metodo,
            ValidateSigningMeans = validar,
            CompanyVisibility = OtCompanyVisibility.WholeNetwork,
        };

    [Fact]
    public async Task ConFirmaDelBaulDeLaCompania_SeRegistra_YQuedaGuardadaLaFirma()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var firma = await SeedFirmaAsync(ctx, CompanyA, "1020304050");

        var result = await Handler(ctx).HandleAsync(Alta("1020304050", CompanyA, firma), Ct);

        result.IsValid.Should().BeTrue();
        var reader = new DbMandateSignerReader(ctx);
        (await reader.GetByIdAsync(result.MandateSignerId!.Value, Ct))!.SignatureVaultId.Should().Be(firma);
    }

    [Fact]
    public async Task FirmaDeOtroTenant_SeRechaza_ContraElTenantDeLaCompania()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var firmaDeB = await SeedFirmaAsync(ctx, CompanyB, "1020304050");

        var result = await Handler(ctx).HandleAsync(Alta("1020304050", CompanyA, firmaDeB), Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Field.Should().Be("signatureVaultId");
    }

    [Fact]
    public async Task FirmaDeOtraPersonaOInactiva_SeRechaza()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var otraPersona = await SeedFirmaAsync(ctx, CompanyA, "9999999999");
        var inactiva = await SeedFirmaAsync(ctx, CompanyA, "1020304050", estado: "revocada");

        (await Handler(ctx).HandleAsync(Alta("1020304050", CompanyA, otraPersona), Ct)).IsValid.Should().BeFalse();
        (await Handler(ctx).HandleAsync(Alta("1020304050", CompanyA, inactiva), Ct)).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ConBaul_SinFirmaEnElBaul_AunConCorreo_SeRechazaConMensajeDeFaltaDeFirmaDelBaul()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();

        var result = await Handler(ctx).HandleAsync(Alta("1020304050", CompanyA, null), Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Message.Should().Be(CreateMandateSignerHandler.SinBaulParaOtMessage);
    }

    [Fact]
    public async Task ExclusividadCompaniaOrganismo_TienePrioridadSobreLaFirma()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var firma1 = await SeedFirmaAsync(ctx, CompanyA, "1020304050");
        var firma2 = await SeedFirmaAsync(ctx, CompanyA, "1020304051");
        (await Handler(ctx).HandleAsync(Alta("1020304050", CompanyA, firma1), Ct)).IsValid.Should().BeTrue();

        var segundo = await Handler(ctx).HandleAsync(Alta("1020304051", CompanyA, firma2), Ct);

        segundo.IsValid.Should().BeFalse();
        segundo.Errors.Should().ContainSingle().Which.Message
            .Should().Contain("Ya existe un mandatario para esta empresa en este organismo.");
    }

    [Fact]
    public async Task FirmaConVariasCompanias_SeRechaza()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var firma = await SeedFirmaAsync(ctx, CompanyA, "1020304050");
        var cmd = new CreateMandateSignerCommand
        {
            TransitOfficeId = Office,
            FullName = "Ana Restrepo",
            DocumentNumber = "1020304050",
            CompanyTenantIds = [CompanyA, CompanyB],
            SignatureVaultId = firma,
            SignatureMethod = "baul",
            ValidateSigningMeans = true,
            CompanyVisibility = OtCompanyVisibility.WholeNetwork,
        };

        var result = await Handler(ctx).HandleAsync(cmd, Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Field.Should().Be("signatureVaultId");
    }

    // ── Ajuste HU #13123: el OT no envía el baúl; el backend resuelve el medio de firma ────────────

    [Fact]
    public async Task SinVault_ConFirmaVigenteEnElBaulDeLaCompania_SeRegistraYVinculaLaFirma()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var firma = await SeedFirmaAsync(ctx, CompanyA, "1020304050");

        var result = await Handler(ctx).HandleAsync(Alta("1020304050", CompanyA, null), Ct);

        result.IsValid.Should().BeTrue();
        result.SigningMeans.Should().Be("baul");
        var reader = new DbMandateSignerReader(ctx);
        (await reader.GetByIdAsync(result.MandateSignerId!.Value, Ct))!.SignatureVaultId.Should().Be(firma);
    }

    [Fact]
    public async Task ConBiometria_SeRegistraSinFirmaEnBaul_SinExigirAprobacionPrevia()
    {
        // HU #13246 — la biometría se elige explícita y no exige aprobación previa: la validación propia se lanza al guardar.
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();

        var result = await Handler(ctx).HandleAsync(Alta("1020304050", CompanyA, null, metodo: "biometria"), Ct);

        result.IsValid.Should().BeTrue();
        result.SigningMeans.Should().Be("biometria");
        var reader = new DbMandateSignerReader(ctx);
        (await reader.GetByIdAsync(result.MandateSignerId!.Value, Ct))!.SignatureVaultId.Should().BeNull();
    }

    [Fact]
    public async Task ConBiometria_LaAprobacionDeOtroRolConElMismoDocumento_NoCambiaNada_YSiempreSeLanzaLaPropia()
    {
        // HU #13246/#13247 — una aprobación previa del documento (comprador, vendedor, prevalidación) ya no habilita ni
        // se reutiliza: se lanza SU validación.
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        await SeedBiometriaAsync(ctx, CompanyA, "1020304050", BiometricEstados.Aprobado);
        var launcher = new StubMandateSignerIdentityLauncher();
        var handler = new CreateMandateSignerHandler(
            new DbTransitOfficeOperationalStatusReader(ctx),
            new DbMandateSignerReader(ctx),
            new MandateSignerRepository(ctx),
            new DbSignatureVaultReader(ctx),
            launcher);

        var result = await handler.HandleAsync(Alta("1020304050", CompanyA, null, metodo: "biometria"), Ct);

        result.IsValid.Should().BeTrue();
        launcher.Calls.Should().ContainSingle().Which.TenantId.Should().Be(CompanyA);
    }

    [Fact]
    public async Task SinFormaDeFirma_ConFirmaVigenteEnElBaul_ConservaLaResolucionYFijaBaul()
    {
        // Sin signatureMethod explicito el OT conserva la resolucion en servidor de HU #13123.
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var firma = await SeedFirmaAsync(ctx, CompanyA, "1020304050");

        var result = await Handler(ctx).HandleAsync(Alta("1020304050", CompanyA, null, metodo: null), Ct);

        result.IsValid.Should().BeTrue();
        result.SigningMeans.Should().Be("baul");
        var fila = await ctx.MandateSigners.AsNoTracking().SingleAsync(Ct);
        fila.SignatureMethod.Should().Be("baul");
        fila.SignatureVaultId.Should().Be(firma);
    }

    [Fact]
    public async Task SinFormaDeFirma_SinBaulNiBiometriaAprobada_422ConMensajeDeFaltaDeMedio()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();

        var result = await Handler(ctx).HandleAsync(Alta("1020304050", CompanyA, null, metodo: null), Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Message.Should().Be(CreateMandateSignerHandler.SinMedioParaOtMessage);
    }

    [Fact]
    public async Task SinVault_FirmaDelBaulDeOtraCompania_NoCuenta()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        await SeedFirmaAsync(ctx, CompanyB, "1020304050");

        var result = await Handler(ctx).HandleAsync(Alta("1020304050", CompanyA, null), Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Message.Should().Be(CreateMandateSignerHandler.SinBaulParaOtMessage);
    }

    [Fact]
    public async Task SinVault_FirmaRevocadaEnElBaul_NoCuenta()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        await SeedFirmaAsync(ctx, CompanyA, "1020304050", estado: "revocada");

        (await Handler(ctx).HandleAsync(Alta("1020304050", CompanyA, null), Ct)).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task SinVault_ConVariasCompanias_SeRechazaConMensajeClaro()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        await SeedFirmaAsync(ctx, CompanyA, "1020304050");
        var cmd = new CreateMandateSignerCommand
        {
            TransitOfficeId = Office,
            FullName = "Ana Restrepo",
            DocumentNumber = "1020304050",
            CompanyTenantIds = [CompanyA, CompanyB],
            SignatureMethod = "baul",
            ValidateSigningMeans = true,
            CompanyVisibility = OtCompanyVisibility.WholeNetwork,
        };

        var result = await Handler(ctx).HandleAsync(cmd, Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Message.Should().Be(CreateMandateSignerHandler.VariasCompaniasSinBaulMessage);
    }

    [Fact]
    public async Task SinVault_LaFirmaFisicaTransitoriaNoExcusaElAltaDelOt()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var cmd = new CreateMandateSignerCommand
        {
            TransitOfficeId = Office,
            FullName = "Ana Restrepo",
            DocumentNumber = "1020304050",
            CompanyTenantIds = [CompanyA],
            TransitOfficeIds = [Office],
            PhysicalSignatureOfficeIds = [Office],
            SignatureMethod = "baul",
            ValidateSigningMeans = true,
            CompanyVisibility = OtCompanyVisibility.WholeNetwork,
        };

        (await Handler(ctx).HandleAsync(cmd, Ct)).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ConVaultExplicito_ConservaLaValidacionActual_AunConBiometriaAprobada()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        await SeedBiometriaAsync(ctx, CompanyA, "1020304050", BiometricEstados.Aprobado);
        var firmaDeB = await SeedFirmaAsync(ctx, CompanyB, "1020304050");

        var result = await Handler(ctx).HandleAsync(Alta("1020304050", CompanyA, firmaDeB), Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Field.Should().Be("signatureVaultId");
    }

    [Fact]
    public async Task SinLaBanderaDelOt_ElHandlerConservaSuComportamientoPrevio()
    {
        // El flujo de la compañía valida antes de delegar y no activa la bandera: no se valida dos veces.
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();

        var result = await Handler(ctx).HandleAsync(Alta("1020304050", CompanyA, null, validar: false), Ct);

        result.IsValid.Should().BeTrue();
    }
}
