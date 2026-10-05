using Flit.Admin.Application.Companies.MandateSigners;
using Flit.Admin.Application.Companies.MandateSigners.CompanyMandateSigners;
using Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.IdentityValidation;
using Flit.Admin.Application.Companies.MandateSigners.InactivateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.ReactivateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.UpdateMandateSigner;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// HU #13246 (Feature #13245, Épica #13090) — disparo y reenvío de la validación de identidad PROPIA del mandatario.
/// Ejercita los handlers reales sobre InMemory con un doble del puerto del lanzador (el flujo Kyverum/mock real lo cubren
/// los tests de Tramites e Infraestructura). <para>Uso: <c>creator.HandleAsync(alta biometría con correo)</c> ⇒ el doble
/// recibe un lanzamiento con el tenant de la compañía y <c>result.Identity == Sent</c>.</para>
/// </summary>
public sealed class MandatarioIdentidadPropiaDisparoTests
{
    private static readonly Guid Office = MandateSignerHandlerTests.Office;
    private static readonly Guid OtTenant = MandateSignerHandlerTests.OtTenant;
    private static readonly Guid CompanyA = MandateSignerHandlerTests.CompanyA;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CreateMandateSignerHandler Creator(FlitDbContext ctx, StubMandateSignerIdentityLauncher launcher) =>
        new(
            new DbTransitOfficeOperationalStatusReader(ctx),
            new DbMandateSignerReader(ctx),
            new MandateSignerRepository(ctx),
            new DbSignatureVaultReader(ctx),
            launcher);

    private static UpdateMandateSignerHandler Updater(FlitDbContext ctx, StubMandateSignerIdentityLauncher launcher) =>
        new(
            new DbTransitOfficeOperationalStatusReader(ctx),
            new DbMandateSignerReader(ctx),
            new MandateSignerRepository(ctx),
            identityLauncher: launcher);

    private static ResendMandateSignerIdentityHandler Resender(
        FlitDbContext ctx, StubMandateSignerIdentityLauncher launcher) =>
        new(new DbMandateSignerReader(ctx), new DbTransitOfficeOperationalStatusReader(ctx), launcher);

    private static CreateMandateSignerCommand Alta(
        string? metodo = "biometria",
        string? email = "ana@flit.test",
        string modelo = "natural",
        string documento = "1020304050",
        Guid? vault = null,
        bool desdeOt = true) =>
        new()
        {
            TransitOfficeId = Office,
            FullName = "Ana Restrepo",
            DocumentNumber = modelo == "formato_blanco" ? string.Empty : documento,
            DocumentType = "CC",
            CompanyTenantIds = [CompanyA],
            TransitOfficeIds = [Office],
            Email = email,
            SignerModel = modelo,
            SignatureMethod = modelo == "natural" ? metodo : null,
            SignatureVaultId = vault,
            ValidateSigningMeans = desdeOt,
            CompanyVisibility = OtCompanyVisibility.WholeNetwork,
        };

    private static UpdateMandateSignerCommand Editar(
        Guid id,
        string metodo = "biometria",
        string documento = "1020304050",
        string tipo = "CC",
        string? email = "ana@flit.test") =>
        new()
        {
            TransitOfficeId = Office,
            MandateSignerId = id,
            FullName = "Ana Restrepo",
            DocumentNumber = documento,
            DocumentType = tipo,
            Email = email,
            CompanyTenantIds = [CompanyA],
            SignatureMethod = metodo,
            CompanyVisibility = OtCompanyVisibility.WholeNetwork,
        };

    private static async Task<Guid> SeedFirmaAsync(FlitDbContext ctx, string documento)
    {
        var id = Guid.NewGuid();
        var hoy = DateOnly.FromDateTime(DateTimeOffset.UtcNow.AddHours(-5).Date);
        ctx.SignatureVault.Add(new SignatureVaultEntity
        {
            Id = id,
            TenantId = CompanyA,
            DocumentType = "CC",
            DocumentNumber = documento,
            FullName = "Ana Restrepo",
            SignatureHash = "sha",
            StoragePath = "vault/f.png",
            StorageSha256 = "sha",
            Estado = "activa",
            VigenciaDesde = hoy.AddDays(-1),
            VigenciaHasta = hoy.AddYears(1),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync(Ct);
        return id;
    }

    // ── AC1/AC2 — alta de la compañía y del hub OT ─────────────────────────────────────────────────

    [Fact]
    public async Task Alta_ConBiometriaYCorreo_LanzaLaValidacionPropia_EnElTenantDeLaCompania()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var launcher = new StubMandateSignerIdentityLauncher();

        var result = await Creator(ctx, launcher).HandleAsync(Alta(), Ct);

        result.IsValid.Should().BeTrue();
        result.Identity.Should().Be(MandateSignerIdentityOutcome.Sent);
        var call = launcher.Calls.Should().ContainSingle().Subject;
        call.MandateSignerId.Should().Be(result.MandateSignerId!.Value);
        call.TenantId.Should().Be(CompanyA).And.NotBe(OtTenant); // AC2: nunca el tenant del organismo
        call.DocumentType.Should().Be("CC");
        call.DocumentNumber.Should().Be("1020304050");
        call.Email.Should().Be("ana@flit.test");
    }

    [Fact]
    public async Task AltaDesdeLaCompania_ConBiometria_LanzaLaValidacionPropia()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var launcher = new StubMandateSignerIdentityLauncher();
        var company = new CreateCompanyMandateSignerHandler(
            new DbMandateSignerReader(ctx), Creator(ctx, launcher), new DbSignatureVaultReader(ctx));

        var result = await company.HandleAsync(
            CompanyA,
            new CompanyMandateSignerRequest(
                "Ana Restrepo", "1020304050", [Office], "CC", "ana@flit.test",
                SignerModel: "natural", SignatureMethod: "biometria"),
            null,
            Ct);

        result.IsValid.Should().BeTrue();
        result.Identity.Should().Be(MandateSignerIdentityOutcome.Sent);
        launcher.Calls.Should().ContainSingle().Which.TenantId.Should().Be(CompanyA);
    }

    // ── AC5/AC6 — correo obligatorio y modelos que no validan ──────────────────────────────────────

    [Fact]
    public async Task Alta_ConBiometriaSinCorreo_422ConElCampoEmail_YNoLanza()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var launcher = new StubMandateSignerIdentityLauncher();

        var result = await Creator(ctx, launcher).HandleAsync(Alta(email: " "), Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.Field == "email" && e.Message == MandateSignerModelRules.CorreoRequeridoConBiometriaMessage);
        launcher.Calls.Should().BeEmpty();
        (await ctx.MandateSigners.AnyAsync(Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Alta_ConBaul_SinCorreo_SeGuardaSinErrores_YNoLanza()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var firma = await SeedFirmaAsync(ctx, "1020304050");
        var launcher = new StubMandateSignerIdentityLauncher();

        var result = await Creator(ctx, launcher).HandleAsync(Alta(metodo: "baul", email: null, vault: firma), Ct);

        result.IsValid.Should().BeTrue();
        result.Identity.Should().Be(MandateSignerIdentityOutcome.NotAttempted);
        launcher.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("juridica")]
    [InlineData("formato_blanco")]
    public async Task Alta_JuridicaOFormatoEnBlanco_NoLanzaValidacion(string modelo)
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var launcher = new StubMandateSignerIdentityLauncher();

        var result = await Creator(ctx, launcher).HandleAsync(Alta(modelo: modelo, email: null), Ct);

        result.IsValid.Should().BeTrue();
        result.Identity.Should().Be(MandateSignerIdentityOutcome.NotAttempted);
        launcher.Calls.Should().BeEmpty();
    }

    // ── AC7 — falla transitoria: el mandatario se guarda y la respuesta lo indica ──────────────────

    [Fact]
    public async Task Alta_ConProveedorCaido_GuardaAlMandatario_YLaRespuestaIndicaCola()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var launcher = new StubMandateSignerIdentityLauncher { Outcome = MandateSignerIdentityLaunchOutcome.Queued };

        var result = await Creator(ctx, launcher).HandleAsync(Alta(), Ct);

        result.IsValid.Should().BeTrue();
        result.Identity.Should().Be(MandateSignerIdentityOutcome.Queued);
        (await ctx.MandateSigners.AsNoTracking().CountAsync(Ct)).Should().Be(1);
    }

    [Fact]
    public async Task Alta_SiElLanzadorFalla_ElMandatarioSeGuarda_YElDesenlaceEsFailed()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var launcher = new StubMandateSignerIdentityLauncher { Throw = new InvalidOperationException("boom") };

        var result = await Creator(ctx, launcher).HandleAsync(Alta(), Ct);

        result.IsValid.Should().BeTrue();
        result.Identity.Should().Be(MandateSignerIdentityOutcome.Failed);
        (await ctx.MandateSigners.AsNoTracking().CountAsync(Ct)).Should().Be(1);
    }

    [Fact]
    public async Task Alta_DosLanzamientosSimultaneos_LaCarreraSeReportaComoUnaSolaActiva()
    {
        // El índice único por mandatario (DDL 129) deja una sola en vuelo: el que pierde recibe AlreadyInFlight y se
        // trata como enviada (no se duplica ni se marca como falla).
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var launcher = new StubMandateSignerIdentityLauncher { Outcome = MandateSignerIdentityLaunchOutcome.AlreadyInFlight };

        var result = await Creator(ctx, launcher).HandleAsync(Alta(), Ct);

        result.Identity.Should().Be(MandateSignerIdentityOutcome.Sent);
    }

    // ── AC3 — edición: cambio de documento y de baúl a biometría ───────────────────────────────────

    [Fact]
    public async Task Edicion_CambiarElNumeroODelTipoDeDocumento_LanzaUnaNueva()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var seed = new StubMandateSignerIdentityLauncher();
        var id = (await Creator(ctx, seed).HandleAsync(Alta(), Ct)).MandateSignerId!.Value;
        var launcher = new StubMandateSignerIdentityLauncher();

        var porNumero = await Updater(ctx, launcher).HandleAsync(Editar(id, documento: "1020304051"), Ct);
        var porTipo = await Updater(ctx, launcher).HandleAsync(Editar(id, documento: "1020304051", tipo: "CE"), Ct);

        porNumero.Outcome.Should().Be(UpdateMandateSignerOutcome.Updated);
        porNumero.Identity.Should().Be(MandateSignerIdentityOutcome.Sent);
        porTipo.Identity.Should().Be(MandateSignerIdentityOutcome.Sent);
        launcher.Calls.Should().HaveCount(2);
        launcher.Calls[0].DocumentNumber.Should().Be("1020304051");
        launcher.Calls[1].DocumentType.Should().Be("CE");
        launcher.Calls.Should().OnlyContain(c => c.TenantId == CompanyA);
    }

    [Fact]
    public async Task Edicion_SinCambiarDocumentoNiFormaDeFirma_NoLanza_NiPorCambiarElCorreo()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var id = (await Creator(ctx, new StubMandateSignerIdentityLauncher()).HandleAsync(Alta(), Ct)).MandateSignerId!.Value;
        var launcher = new StubMandateSignerIdentityLauncher();

        var result = await Updater(ctx, launcher).HandleAsync(Editar(id, email: "otro@flit.test"), Ct);

        result.Outcome.Should().Be(UpdateMandateSignerOutcome.Updated);
        result.Identity.Should().Be(MandateSignerIdentityOutcome.NotAttempted);
        launcher.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Edicion_PasarDeBaulABiometria_LanzaUnaNueva()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var firma = await SeedFirmaAsync(ctx, "1020304050");
        var id = (await Creator(ctx, new StubMandateSignerIdentityLauncher())
            .HandleAsync(Alta(metodo: "baul", vault: firma), Ct)).MandateSignerId!.Value;
        var launcher = new StubMandateSignerIdentityLauncher();

        var result = await Updater(ctx, launcher).HandleAsync(Editar(id, metodo: "biometria"), Ct);

        result.Outcome.Should().Be(UpdateMandateSignerOutcome.Updated);
        result.Identity.Should().Be(MandateSignerIdentityOutcome.Sent);
        launcher.Calls.Should().ContainSingle().Which.MandateSignerId.Should().Be(id);
    }

    [Fact]
    public async Task Edicion_ConBiometriaSinCorreo_422()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var id = (await Creator(ctx, new StubMandateSignerIdentityLauncher()).HandleAsync(Alta(), Ct)).MandateSignerId!.Value;

        var result = await Updater(ctx, new StubMandateSignerIdentityLauncher())
            .HandleAsync(Editar(id, email: null), Ct);

        result.Outcome.Should().Be(UpdateMandateSignerOutcome.ValidationFailed);
        result.Errors.Should().Contain(e => e.Field == "email");
    }

    [Fact]
    public async Task Reactivar_UnMandatarioInactivado_NoLanzaNingunaValidacion()
    {
        // HU #13246 AC3 (decisión 3 del Líder Técnico): reactivar NO dispara; su validación, si la tenía, sigue siendo la suya.
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var launcher = new StubMandateSignerIdentityLauncher();
        var id = (await Creator(ctx, launcher).HandleAsync(Alta(), Ct)).MandateSignerId!.Value;
        launcher.Calls.Should().ContainSingle();
        var otStatus = new DbTransitOfficeOperationalStatusReader(ctx);
        var reader = new DbMandateSignerReader(ctx);
        var repo = new MandateSignerRepository(ctx);
        await new InactivateMandateSignerHandler(otStatus, reader, repo).HandleAsync(
            new InactivateMandateSignerCommand { TransitOfficeId = Office, MandateSignerId = id }, Ct);

        var outcome = await new ReactivateMandateSignerHandler(otStatus, reader, repo).HandleAsync(
            new ReactivateMandateSignerCommand { TransitOfficeId = Office, MandateSignerId = id }, Ct);

        outcome.Should().Be(ReactivateMandateSignerOutcome.Reactivated);
        launcher.Calls.Should().ContainSingle("reactivar no lanza una validación nueva");
    }

    // ── AC3/AC6 — reenvío ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reenvio_ConBiometria_LanzaUnaNueva_EnElTenantDeLaCompania()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var id = (await Creator(ctx, new StubMandateSignerIdentityLauncher()).HandleAsync(Alta(), Ct)).MandateSignerId!.Value;
        var launcher = new StubMandateSignerIdentityLauncher();

        var porOt = await Resender(ctx, launcher).HandleAsync(id, Office, null, Ct);
        var porCompania = await Resender(ctx, launcher).HandleAsync(id, null, CompanyA, Ct);

        porOt.Outcome.Should().Be(ResendMandateSignerIdentityOutcome.Sent);
        porCompania.Outcome.Should().Be(ResendMandateSignerIdentityOutcome.Sent);
        launcher.Calls.Should().HaveCount(2).And.OnlyContain(c => c.TenantId == CompanyA && c.MandateSignerId == id);
    }

    [Fact]
    public async Task Reenvio_ConColaDelProveedor_ResponderQueued()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var id = (await Creator(ctx, new StubMandateSignerIdentityLauncher()).HandleAsync(Alta(), Ct)).MandateSignerId!.Value;
        var launcher = new StubMandateSignerIdentityLauncher { Outcome = MandateSignerIdentityLaunchOutcome.Queued };

        (await Resender(ctx, launcher).HandleAsync(id, Office, null, Ct)).Outcome
            .Should().Be(ResendMandateSignerIdentityOutcome.Queued);
    }

    [Fact]
    public async Task Reenvio_ConBaul_409_NoRequiereValidacion()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var firma = await SeedFirmaAsync(ctx, "1020304050");
        var id = (await Creator(ctx, new StubMandateSignerIdentityLauncher())
            .HandleAsync(Alta(metodo: "baul", vault: firma), Ct)).MandateSignerId!.Value;
        var launcher = new StubMandateSignerIdentityLauncher();

        (await Resender(ctx, launcher).HandleAsync(id, Office, null, Ct)).Outcome
            .Should().Be(ResendMandateSignerIdentityOutcome.NoRequiereValidacion);
        launcher.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("juridica")]
    [InlineData("formato_blanco")]
    public async Task Reenvio_JuridicaOFormatoEnBlanco_409_NoRequiereValidacion(string modelo)
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var id = (await Creator(ctx, new StubMandateSignerIdentityLauncher())
            .HandleAsync(Alta(modelo: modelo, email: null), Ct)).MandateSignerId!.Value;

        (await Resender(ctx, new StubMandateSignerIdentityLauncher()).HandleAsync(id, Office, null, Ct)).Outcome
            .Should().Be(ResendMandateSignerIdentityOutcome.NoRequiereValidacion);
    }

    [Fact]
    public async Task Reenvio_DeUnMandatarioEliminado_404()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var id = (await Creator(ctx, new StubMandateSignerIdentityLauncher()).HandleAsync(Alta(), Ct)).MandateSignerId!.Value;
        var fila = await ctx.MandateSigners.SingleAsync(Ct);
        fila.DeletedAt = DateTimeOffset.UtcNow;
        await ctx.SaveChangesAsync(Ct);
        var launcher = new StubMandateSignerIdentityLauncher();

        (await Resender(ctx, launcher).HandleAsync(id, Office, null, Ct)).Outcome
            .Should().Be(ResendMandateSignerIdentityOutcome.NotFound);
        launcher.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Reenvio_DeOtroOrganismo_404()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var id = (await Creator(ctx, new StubMandateSignerIdentityLauncher()).HandleAsync(Alta(), Ct)).MandateSignerId!.Value;

        (await Resender(ctx, new StubMandateSignerIdentityLauncher()).HandleAsync(id, Guid.NewGuid(), null, Ct)).Outcome
            .Should().Be(ResendMandateSignerIdentityOutcome.NotFound);
    }

    [Fact]
    public async Task Reenvio_SinCorreoEnLaFicha_422()
    {
        // Mandatario legado con biometría efectiva y sin correo (anterior a la regla): hay que completar el correo.
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var id = (await Creator(ctx, new StubMandateSignerIdentityLauncher()).HandleAsync(Alta(), Ct)).MandateSignerId!.Value;
        var fila = await ctx.MandateSigners.SingleAsync(Ct);
        fila.Email = null;
        await ctx.SaveChangesAsync(Ct);

        (await Resender(ctx, new StubMandateSignerIdentityLauncher()).HandleAsync(id, Office, null, Ct)).Outcome
            .Should().Be(ResendMandateSignerIdentityOutcome.CorreoRequerido);
    }

    [Fact]
    public async Task Reenvio_ProveedorRechazaDeFormaDefinitiva_ProveedorError()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var id = (await Creator(ctx, new StubMandateSignerIdentityLauncher()).HandleAsync(Alta(), Ct)).MandateSignerId!.Value;
        var launcher = new StubMandateSignerIdentityLauncher { Outcome = MandateSignerIdentityLaunchOutcome.Failed };

        (await Resender(ctx, launcher).HandleAsync(id, Office, null, Ct)).Outcome
            .Should().Be(ResendMandateSignerIdentityOutcome.ProveedorError);
    }
}
