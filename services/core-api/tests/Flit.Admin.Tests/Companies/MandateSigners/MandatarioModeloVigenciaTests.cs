using Flit.Admin.Application.Companies.MandateSigners;
using Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.InactivateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.ListCompanyMandateSigners;
using Flit.Admin.Application.Companies.MandateSigners.ListMandateSigners;
using Flit.Admin.Application.Companies.MandateSigners.UpdateMandateSigner;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
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
/// HU #13129 (Epic #13090, F2) — reglas de validación por modelo (natural, jurídica, formato en blanco),
/// forma de firma, vigencia propia y estado de vigencia calculado. Ejercita los handlers reales sobre
/// InMemory. <para>Uso: <c>handler.HandleAsync(command con SignerModel = "juridica" y SignatureMethod =
/// "baul")</c> ⇒ <c>IsValid == false</c> con el campo <c>signatureMethod</c>.</para>
/// </summary>
public sealed class MandatarioModeloVigenciaTests
{
    private static readonly Guid Office = MandateSignerHandlerTests.Office;
    private static readonly Guid CompanyA = MandateSignerHandlerTests.CompanyA;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static DateOnly Hoy => ColombiaTime.Today(TimeProvider.System);

    /// <summary>
    /// Contexto con una validación biométrica APROBADA y vigente para el documento de las pruebas: el alta del
    /// OT (ValidateSigningMeans) solo deja nacer al mandatario con biometría si ya la tiene (HU #13123).
    /// </summary>
    private static FlitDbContext NewCtx(bool conBiometria = true)
    {
        var ctx = MandateSignerHandlerTests.NewSeededContext();
        if (conBiometria)
        {
            ctx.ProcedureInstanceBiometricValidations.Add(new ProcedureInstanceBiometricValidation
            {
                Id = Guid.NewGuid(),
                TenantId = CompanyA,
                DocumentType = "CC",
                DocumentNumber = "1020304050",
                Status = BiometricEstados.Aprobado,
                Provider = BiometricProviders.Kyverum,
                TokenHash = "h",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                ValidatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                ValidUntil = DateTimeOffset.UtcNow.AddDays(29),
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            });
            ctx.SaveChanges();
        }

        return ctx;
    }

    private static CreateMandateSignerHandler Creator(FlitDbContext ctx) =>
        new(
            new DbTransitOfficeOperationalStatusReader(ctx),
            new DbMandateSignerReader(ctx),
            new MandateSignerRepository(ctx),
            new DbSignatureVaultReader(ctx),
            new DbMandateSignerBiometricApprovalReader(ctx));

    private static UpdateMandateSignerHandler Updater(FlitDbContext ctx) =>
        new(
            new DbTransitOfficeOperationalStatusReader(ctx),
            new DbMandateSignerReader(ctx),
            new MandateSignerRepository(ctx));

    private static ListMandateSignersHandler Lister(FlitDbContext ctx) => new(new DbMandateSignerReader(ctx));

    private static Task<IReadOnlyList<MandateSignerResponse>> ListAsync(FlitDbContext ctx) =>
        Lister(ctx).HandleAsync(
            new ListMandateSignersQuery { TransitOfficeId = Office, Visibility = OtCompanyVisibility.WholeNetwork }, Ct);

    private static async Task<Guid> SeedFirmaAsync(FlitDbContext ctx, string documento)
    {
        var id = Guid.NewGuid();
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
            VigenciaDesde = Hoy.AddDays(-1),
            VigenciaHasta = Hoy.AddYears(1),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync(Ct);
        return id;
    }

    private static CreateMandateSignerCommand Natural(
        string? metodo,
        string? tipoVigencia = null,
        DateOnly? desde = null,
        DateOnly? hasta = null,
        Guid? firma = null,
        string documento = "1020304050") =>
        new()
        {
            TransitOfficeId = Office,
            FullName = "Ana Restrepo",
            DocumentNumber = documento,
            DocumentType = "CC",
            CompanyTenantIds = [CompanyA],
            TransitOfficeIds = [Office],
            SignerModel = "natural",
            SignatureMethod = metodo,
            ValidityKind = tipoVigencia,
            ValidFrom = desde,
            ValidTo = hasta,
            SignatureVaultId = firma,
            ValidateSigningMeans = true,
            CompanyVisibility = OtCompanyVisibility.WholeNetwork,
        };

    private static CreateMandateSignerCommand SinPersona(
        string modelo,
        string? metodo = null,
        string? tipoVigencia = null,
        DateOnly? desde = null,
        DateOnly? hasta = null,
        string? correo = null) =>
        new()
        {
            TransitOfficeId = Office,
            FullName = modelo == "juridica" ? "Operadora de Tránsito UT" : "cualquier nombre",
            DocumentNumber = modelo == "juridica" ? "900123456" : string.Empty,
            CompanyTenantIds = [CompanyA],
            TransitOfficeIds = [Office],
            SignerModel = modelo,
            SignatureMethod = metodo,
            ValidityKind = tipoVigencia,
            ValidFrom = desde,
            ValidTo = hasta,
            Email = correo,
            ValidateSigningMeans = true,
            CompanyVisibility = OtCompanyVisibility.WholeNetwork,
        };

    // ── AC1 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_PersonaNatural_ConBaulYVigenciaFija_SeGuardaSinFechas_YEsVigente()
    {
        await using var ctx = NewCtx();
        var firma = await SeedFirmaAsync(ctx, "1020304050");

        var result = await Creator(ctx).HandleAsync(Natural("baul", firma: firma), Ct);

        result.IsValid.Should().BeTrue(string.Join(";", result.Errors.Select(e => e.Message)));
        var fila = await ctx.MandateSigners.AsNoTracking().SingleAsync(Ct);
        fila.SignerModel.Should().Be("natural");
        fila.SignatureMethod.Should().Be("baul");
        fila.SignatureVaultId.Should().Be(firma);
        fila.ValidityKind.Should().Be("fixed");
        fila.ValidFrom.Should().BeNull();
        fila.ValidTo.Should().BeNull();

        var respuesta = (await ListAsync(ctx)).Single();
        respuesta.SignerModel.Should().Be("natural");
        respuesta.SignatureMethod.Should().Be("baul");
        respuesta.ValidityKind.Should().Be("fixed");
        respuesta.ValidityStatus.Should().Be("vigente");
    }

    // ── AC2 ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(-5, 30, "vigente")]
    [InlineData(-5, 7, "por_vencer")]
    [InlineData(-30, -1, "vencido")]
    public async Task AC2_PersonaNatural_ConBiometriaYRango_SeGuarda_YElEstadoSigueElRango(
        int desde, int hasta, string estado)
    {
        await using var ctx = NewCtx();

        var result = await Creator(ctx).HandleAsync(
            Natural("biometria", "range", Hoy.AddDays(desde), Hoy.AddDays(hasta)), Ct);

        result.IsValid.Should().BeTrue(string.Join(";", result.Errors.Select(e => e.Message)));
        var fila = await ctx.MandateSigners.AsNoTracking().SingleAsync(Ct);
        fila.SignatureMethod.Should().Be("biometria");
        fila.ValidityKind.Should().Be("range");
        fila.ValidFrom.Should().Be(Hoy.AddDays(desde));
        fila.ValidTo.Should().Be(Hoy.AddDays(hasta));
        (await ListAsync(ctx)).Single().ValidityStatus.Should().Be(estado);
    }

    // ── AC3 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC3_PersonaJuridica_SinFormaDeFirmaNiVigencia_SeAcepta_ConNitYSinIdentidad()
    {
        await using var ctx = NewCtx();
        // Aunque exista una validación biométrica aprobada para ese número, a una jurídica no se le asocia.
        ctx.ProcedureInstanceBiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = CompanyA,
            DocumentType = "NIT",
            DocumentNumber = "900123456",
            Status = BiometricEstados.Aprobado,
            Provider = BiometricProviders.Kyverum,
            TokenHash = "h",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            ValidatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            ValidUntil = DateTimeOffset.UtcNow.AddDays(29),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        });
        await ctx.SaveChangesAsync(Ct);

        var result = await Creator(ctx).HandleAsync(SinPersona("juridica"), Ct);

        result.IsValid.Should().BeTrue(string.Join(";", result.Errors.Select(e => e.Message)));
        result.SigningMeans.Should().BeNull();
        var fila = await ctx.MandateSigners.AsNoTracking().SingleAsync(Ct);
        fila.SignerModel.Should().Be("juridica");
        fila.DocumentType.Should().Be("NIT");
        fila.DocumentNumber.Should().Be("900123456");
        fila.SignatureMethod.Should().BeNull();
        fila.SignatureVaultId.Should().BeNull();
        fila.ValidityKind.Should().Be("fixed");
        fila.IdentityValidationRef.Should().BeNull();
        (await ctx.AdminIdentityValidations.CountAsync(Ct)).Should().Be(0);

        var respuesta = (await ListAsync(ctx)).Single();
        respuesta.IdentityStatus.Should().Be("none");
        respuesta.ValidityStatus.Should().Be("vigente");
    }

    [Fact]
    public async Task AC3_FormatoEnBlanco_SinDocumentoNiFirmaNiVigencia_SeAcepta_ConNombreFijado()
    {
        await using var ctx = NewCtx();

        var result = await Creator(ctx).HandleAsync(SinPersona("formato_blanco"), Ct);

        result.IsValid.Should().BeTrue(string.Join(";", result.Errors.Select(e => e.Message)));
        var fila = await ctx.MandateSigners.AsNoTracking().SingleAsync(Ct);
        fila.SignerModel.Should().Be("formato_blanco");
        fila.FullName.Should().Be("Formato en blanco");
        fila.DocumentNumber.Should().BeNull();
        fila.SignatureMethod.Should().BeNull();
        fila.ValidityKind.Should().Be("fixed");
        fila.ValidFrom.Should().BeNull();
        fila.IdentityValidationRef.Should().BeNull();
        fila.IntegrityHash.Should().NotBeNullOrEmpty();
        (await ctx.AdminIdentityValidations.CountAsync(Ct)).Should().Be(0);

        var respuesta = (await ListAsync(ctx)).Single();
        respuesta.FullName.Should().Be("Formato en blanco");
        respuesta.DocumentNumber.Should().BeNull();
        respuesta.IdentityStatus.Should().Be("none");
    }

    // ── AC4 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC4_PersonaNatural_SinFormaDeFirma_FueraDelAltaDelOt_422ConElCampo()
    {
        // Sin ValidateSigningMeans (flujo de la compania) no se infiere el medio: 422 sobre signatureMethod.
        await using var ctx = NewCtx(conBiometria: false);

        var result = await Creator(ctx).HandleAsync(
            new CreateMandateSignerCommand
            {
                TransitOfficeId = Office,
                FullName = "Ana Restrepo",
                DocumentNumber = "1020304050",
                CompanyTenantIds = [CompanyA],
                SignerModel = "natural",
                CompanyVisibility = OtCompanyVisibility.WholeNetwork,
            },
            Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Match<MandateSignerValidationError>(e =>
            e.Field == "signatureMethod" && e.Message == MandateSignerModelRules.MetodoRequeridoMessage);
        (await ctx.MandateSigners.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task AltaOt_SinFormaDeFirma_ResuelveEnServidor_YFijaLaFormaConElMedioHallado()
    {
        await using var ctx = NewCtx();

        var result = await Creator(ctx).HandleAsync(Natural(null), Ct);

        result.IsValid.Should().BeTrue(string.Join(";", result.Errors.Select(e => e.Message)));
        result.SigningMeans.Should().Be("biometria");
        (await ctx.MandateSigners.AsNoTracking().SingleAsync(Ct)).SignatureMethod.Should().Be("biometria");
    }

    [Fact]
    public async Task AltaOt_SinFormaDeFirma_NiMedioDisponible_422SinMedioParaOt()
    {
        await using var ctx = NewCtx(conBiometria: false);

        var result = await Creator(ctx).HandleAsync(Natural(null), Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Message.Should().Be(CreateMandateSignerHandler.SinMedioParaOtMessage);
    }

    [Fact]
    public async Task AC4_PersonaNatural_ConBaulSinFirmaElegida_422()
    {
        await using var ctx = NewCtx();

        var result = await Creator(ctx).HandleAsync(Natural("baul"), Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Field.Should().Be("signatureVaultId");
    }

    [Theory]
    [InlineData(false, true, "validFrom")]
    [InlineData(true, false, "validTo")]
    public async Task AC4_PersonaNatural_ConRangoSinFechas_422ConElCampoQueFalta(
        bool traeDesde, bool traeHasta, string campoEsperado)
    {
        await using var ctx = NewCtx();

        var result = await Creator(ctx).HandleAsync(
            Natural("biometria", "range", traeDesde ? Hoy : null, traeHasta ? Hoy.AddDays(3) : null), Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Field.Should().Be(campoEsperado);
    }

    [Fact]
    public async Task AC4_PersonaNatural_ConRangoSinNingunaFecha_422ConAmbosCampos()
    {
        await using var ctx = NewCtx();

        var result = await Creator(ctx).HandleAsync(Natural("biometria", "range"), Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Select(e => e.Field).Should().BeEquivalentTo(["validFrom", "validTo"]);
    }

    // ── AC5 ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("juridica", "baul", null, "signatureMethod")]
    [InlineData("formato_blanco", "biometria", null, "signatureMethod")]
    [InlineData("juridica", null, "range", "validityKind")]
    [InlineData("formato_blanco", null, "correo", "email")]
    public async Task AC5_JuridicaYFormatoEnBlanco_ConDatosDePersonaNatural_422(
        string modelo, string? metodo, string? extra, string campoEsperado)
    {
        await using var ctx = NewCtx();
        var comando = SinPersona(
            modelo,
            metodo,
            tipoVigencia: extra == "range" ? "range" : null,
            correo: extra == "correo" ? "validacion@x.com" : null);

        var result = await Creator(ctx).HandleAsync(comando, Ct);

        result.IsValid.Should().BeFalse();
        var error = result.Errors.Should().Contain(e => e.Field == campoEsperado).Which;
        error.Message.Should().Contain("Persona natural");
        (await ctx.MandateSigners.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task AC5_JuridicaConFechas_422ParaAmbasFechas()
    {
        await using var ctx = NewCtx();

        var result = await Creator(ctx).HandleAsync(
            SinPersona("juridica", desde: Hoy, hasta: Hoy.AddDays(5)), Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Select(e => e.Field).Should().Contain(["validFrom", "validTo"]);
        result.Errors.Should().OnlyContain(e => e.Message.Contains("Persona natural"));
    }

    [Fact]
    public async Task ModeloDesconocido_422EnSignerModel()
    {
        await using var ctx = NewCtx();

        var result = await Creator(ctx).HandleAsync(SinPersona("empresa"), Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "signerModel");
    }

    // ── AC6 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC6_RangoInvertido_422()
    {
        await using var ctx = NewCtx();

        var result = await Creator(ctx).HandleAsync(
            Natural("biometria", "range", Hoy.AddDays(5), Hoy.AddDays(1)), Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Match<MandateSignerValidationError>(e =>
            e.Field == "validTo" && e.Message == MandateSignerModelRules.RangoInvertidoMessage);
    }

    // ── AC7 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC7_UnMandatarioInactivo_ReportaInactivo_AunConRangoVigente()
    {
        await using var ctx = NewCtx();
        var creado = await Creator(ctx).HandleAsync(
            Natural("biometria", "range", Hoy.AddDays(-1), Hoy.AddDays(60)), Ct);
        (await ListAsync(ctx)).Single().ValidityStatus.Should().Be("vigente");

        var inactivar = new InactivateMandateSignerHandler(
            new DbTransitOfficeOperationalStatusReader(ctx),
            new DbMandateSignerReader(ctx),
            new MandateSignerRepository(ctx));
        await inactivar.HandleAsync(
            new InactivateMandateSignerCommand
            {
                TransitOfficeId = Office,
                MandateSignerId = creado.MandateSignerId!.Value,
                ChangedBy = MandateSignerHandlerTests.Operator,
            },
            Ct);

        (await ListAsync(ctx)).Single().ValidityStatus.Should().Be("inactivo");
    }

    [Fact]
    public async Task AC7_RangoDeUnSoloDia_EsVigenteHoy_EnLaRespuesta()
    {
        await using var ctx = NewCtx();

        var result = await Creator(ctx).HandleAsync(Natural("biometria", "range", Hoy, Hoy), Ct);

        result.IsValid.Should().BeTrue();
        (await ListAsync(ctx)).Single().ValidityStatus.Should().Be("vigente");
    }

    // ── Edición ───────────────────────────────────────────────────────────────

    private static UpdateMandateSignerCommand Editar(
        Guid id,
        string? modelo = null,
        string? metodo = null,
        string? tipoVigencia = null,
        DateOnly? desde = null,
        DateOnly? hasta = null,
        string nombre = "Ana Restrepo",
        string documento = "1020304050") =>
        new()
        {
            TransitOfficeId = Office,
            MandateSignerId = id,
            FullName = nombre,
            DocumentNumber = documento,
            CompanyTenantIds = [CompanyA],
            SignerModel = modelo,
            SignatureMethod = metodo,
            ValidityKind = tipoVigencia,
            ValidFrom = desde,
            ValidTo = hasta,
            CompanyVisibility = OtCompanyVisibility.WholeNetwork,
        };

    [Fact]
    public async Task Editar_SinMandarModeloFormaNiVigencia_ConservaLoGuardado()
    {
        await using var ctx = NewCtx();
        var creado = await Creator(ctx).HandleAsync(
            Natural("biometria", "range", Hoy.AddDays(-1), Hoy.AddDays(60)), Ct);

        var result = await Updater(ctx).HandleAsync(
            Editar(creado.MandateSignerId!.Value, nombre: "Ana Restrepo Gómez"), Ct);

        result.Outcome.Should().Be(UpdateMandateSignerOutcome.Updated);
        var fila = await ctx.MandateSigners.AsNoTracking().SingleAsync(Ct);
        fila.FullName.Should().Be("Ana Restrepo Gómez");
        fila.SignatureMethod.Should().Be("biometria");
        fila.ValidityKind.Should().Be("range");
        fila.ValidFrom.Should().Be(Hoy.AddDays(-1));
        fila.ValidTo.Should().Be(Hoy.AddDays(60));
    }

    [Fact]
    public async Task Editar_RangoInvertido_422_YNoTocaLaFila()
    {
        await using var ctx = NewCtx();
        var creado = await Creator(ctx).HandleAsync(
            Natural("biometria", "range", Hoy.AddDays(-1), Hoy.AddDays(60)), Ct);

        var result = await Updater(ctx).HandleAsync(
            Editar(creado.MandateSignerId!.Value, desde: Hoy.AddDays(10), hasta: Hoy.AddDays(2)), Ct);

        result.Outcome.Should().Be(UpdateMandateSignerOutcome.ValidationFailed);
        result.Errors.Should().ContainSingle().Which.Field.Should().Be("validTo");
        (await ctx.MandateSigners.AsNoTracking().SingleAsync(Ct)).ValidTo.Should().Be(Hoy.AddDays(60));
    }

    [Fact]
    public async Task Editar_PasarAFijaSinFechas_LimpiaElRango()
    {
        await using var ctx = NewCtx();
        var creado = await Creator(ctx).HandleAsync(
            Natural("biometria", "range", Hoy.AddDays(-1), Hoy.AddDays(60)), Ct);

        var result = await Updater(ctx).HandleAsync(
            Editar(creado.MandateSignerId!.Value, tipoVigencia: "fixed"), Ct);

        result.Outcome.Should().Be(UpdateMandateSignerOutcome.Updated);
        var fila = await ctx.MandateSigners.AsNoTracking().SingleAsync(Ct);
        fila.ValidityKind.Should().Be("fixed");
        fila.ValidFrom.Should().BeNull();
        fila.ValidTo.Should().BeNull();
    }

    [Fact]
    public async Task Editar_PasarAFormatoEnBlanco_LimpiaFormaDeFirmaYBaul_YFijaElNombre()
    {
        await using var ctx = NewCtx();
        var firma = await SeedFirmaAsync(ctx, "1020304050");
        var creado = await Creator(ctx).HandleAsync(Natural("baul", firma: firma), Ct);
        creado.IsValid.Should().BeTrue();

        var result = await Updater(ctx).HandleAsync(
            Editar(creado.MandateSignerId!.Value, modelo: "formato_blanco", documento: string.Empty), Ct);

        result.Outcome.Should().Be(UpdateMandateSignerOutcome.Updated);
        var fila = await ctx.MandateSigners.AsNoTracking().SingleAsync(Ct);
        fila.SignerModel.Should().Be("formato_blanco");
        fila.FullName.Should().Be("Formato en blanco");
        fila.DocumentNumber.Should().BeNull();
        fila.SignatureMethod.Should().BeNull();
        fila.SignatureVaultId.Should().BeNull();
    }

    [Fact]
    public async Task Editar_JuridicaConFormaDeFirma_422()
    {
        await using var ctx = NewCtx();
        var creado = await Creator(ctx).HandleAsync(SinPersona("juridica"), Ct);

        var result = await Updater(ctx).HandleAsync(
            Editar(creado.MandateSignerId!.Value, metodo: "biometria", nombre: "Operadora de Tránsito UT", documento: "900123456"),
            Ct);

        result.Outcome.Should().Be(UpdateMandateSignerOutcome.ValidationFailed);
        result.Errors.Should().Contain(e => e.Field == "signatureMethod" && e.Message.Contains("Persona natural"));
    }

    [Fact]
    public async Task Editar_PasarDeBaulABiometria_DesvinculaLaFirmaDelBaul()
    {
        await using var ctx = NewCtx();
        var firma = await SeedFirmaAsync(ctx, "1020304050");
        var creado = await Creator(ctx).HandleAsync(Natural("baul", firma: firma), Ct);

        var result = await Updater(ctx).HandleAsync(Editar(creado.MandateSignerId!.Value, metodo: "biometria"), Ct);

        result.Outcome.Should().Be(UpdateMandateSignerOutcome.Updated);
        var fila = await ctx.MandateSigners.AsNoTracking().SingleAsync(Ct);
        fila.SignatureMethod.Should().Be("biometria");
        fila.SignatureVaultId.Should().BeNull();
    }

    // ── AC8 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC8_UnMandatarioEliminado_NoApareceEnListasNiSelectores_PeroLaFilaSeConserva()
    {
        await using var ctx = NewCtx();
        var creado = await Creator(ctx).HandleAsync(Natural("biometria"), Ct);
        var id = creado.MandateSignerId!.Value;
        (await ListAsync(ctx)).Should().ContainSingle();

        var fila = await ctx.MandateSigners.SingleAsync(Ct);
        fila.DeletedAt = DateTimeOffset.UtcNow;
        fila.DeletedBy = MandateSignerHandlerTests.Operator;
        await ctx.SaveChangesAsync(Ct);
        ctx.ChangeTracker.Clear();

        var reader = new DbMandateSignerReader(ctx);
        (await ListAsync(ctx)).Should().BeEmpty();
        (await new ListCompanyMandateSignersHandler(reader).HandleAsync(CompanyA, Ct)).Should().BeEmpty();
        (await reader.GetByIdAsync(id, Ct)).Should().BeNull();
        (await reader.ListActiveCompanyResolutionsAsync(Office, Ct)).Should().BeEmpty();

        // Editar, inactivar o reactivar un eliminado responde como si no existiera.
        (await Updater(ctx).HandleAsync(Editar(id), Ct)).Outcome.Should().Be(UpdateMandateSignerOutcome.NotFound);

        // La fila y sus vínculos se conservan (historial de trámites y contrato ya generado).
        (await ctx.MandateSigners.AsNoTracking().CountAsync(Ct)).Should().Be(1);
        (await ctx.MandateSignerCompanies.AsNoTracking().CountAsync(Ct)).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AC8_ElSelectorDeTramites_ExcluyeAlEliminado_PeroLaReferenciaHistoricaSeResuelve()
    {
        await using var ctx = NewCtx();
        var creado = await Creator(ctx).HandleAsync(Natural("biometria"), Ct);
        var id = creado.MandateSignerId!.Value;

        var ots = Substitute.For<ITransitOfficeOperationalStatusReader>();
        ots.GetByIdAsync(Office, Arg.Any<CancellationToken>())
            .Returns(new TransitOfficeOperationalStatusItem { Id = Office, HasTenant = false });
        var repo = Substitute.For<IProcedureInstanceRepository>();
        repo.ListBiometricValidationsByPersonAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns((Array.Empty<ProcedureInstanceBiometricValidation>(), 0, false));
        var directorio = new MandateSignerDirectory(ctx, ots, new IdentityVigenciaPorDocumentoResolver(repo));

        (await directorio.GetCandidatesAsync(Office, CompanyA, null, Ct)).Should().ContainSingle();

        var fila = await ctx.MandateSigners.SingleAsync(Ct);
        fila.DeletedAt = DateTimeOffset.UtcNow;
        await ctx.SaveChangesAsync(Ct);
        ctx.ChangeTracker.Clear();

        (await directorio.GetCandidatesAsync(Office, CompanyA, null, Ct)).Should().BeEmpty();

        // HU #13142 (ADR-0066): por defecto la búsqueda por id ya NO devuelve al eliminado (no puede ser el
        // default del OT de nadie nuevo)...
        (await directorio.GetByIdAsync(id, Ct)).Should().BeNull();

        // ...pero el trámite ya firmado conserva su referencia pidiéndola de forma explícita.
        var historico = await directorio.GetByIdAsync(id, incluirEliminados: true, Ct);
        historico.Should().NotBeNull();
        historico!.Nombre.Should().Be("Ana Restrepo");
        historico.Eliminado.Should().BeTrue();
    }

    // ── Compatibilidad de lectura: legados ────────────────────────────────────

    [Fact]
    public async Task UnLegadoSinFormaDeFirma_SeLista_ComoNaturalFijoVigente()
    {
        await using var ctx = NewCtx();
        var ahora = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        ctx.MandateSigners.Add(new MandateSigner
        {
            Id = id,
            TransitOfficeId = Office,
            FullName = "Legado",
            DocumentNumber = "555",
            IntegrityHash = new string('a', 64),
            RegisteredAt = ahora,
            IsActive = true,
            CreatedAt = ahora,
        });
        ctx.MandateSignerTransitOffices.Add(new MandateSignerTransitOffice
        {
            Id = Guid.NewGuid(), MandateSignerId = id, TransitOfficeId = Office, IsActive = true, CreatedAt = ahora,
        });
        await ctx.SaveChangesAsync(Ct);

        var respuesta = (await ListAsync(ctx)).Single();

        respuesta.SignerModel.Should().Be("natural");
        respuesta.SignatureMethod.Should().BeNull();
        respuesta.ValidityKind.Should().Be("fixed");
        respuesta.ValidityStatus.Should().Be("vigente");
    }
}
