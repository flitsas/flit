using System.Text.Json;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances.Estados;

/// <summary>
/// HU #13144 (ADR-0066) — gate de radicación del mandatario: activo, vigente y con firma válida, por modo
/// block/warn/off, con exclusiones y sin tocar el gate de aprobación.
/// </summary>
public sealed class MandatarioGateRadicacionTests
{
    private static readonly Guid Office = Guid.NewGuid();
    private const string DocumentoPii = "1020304050";

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IProcedureTypeRepository _typeRepo = Substitute.For<IProcedureTypeRepository>();
    private readonly ITransitOfficeGrantGate _grant = Substitute.For<ITransitOfficeGrantGate>();
    private readonly IOtOperabilityGate _operable = Substitute.For<IOtOperabilityGate>();
    private readonly IMandateSignerDirectory _directory = Substitute.For<IMandateSignerDirectory>();
    private readonly IMandateRequirementPolicy _policy = Substitute.For<IMandateRequirementPolicy>();
    private readonly ISignatureVaultPolicy _vault = Substitute.For<ISignatureVaultPolicy>();
    private readonly IPersonalizedDocumentResolver _personalized = Substitute.For<IPersonalizedDocumentResolver>();
    private readonly RecordingTransitionRecorder _recorder = new();
    private readonly RecordingTransitionPublisher _publisher = new();
    private readonly CapturingLogger _log = new();

    public MandatarioGateRadicacionTests()
    {
        _grant.IsEnabledForTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _operable.IsOperableAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(true);
        _personalized.ResolveAsync(Arg.Any<Guid>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(PersonalizedDocumentResolution.Empty);
        Config("signer");
    }

    private TramiteLifecycleService Sut(TramiteValidationMode modo, bool conDirectorio = true) =>
        new(_repo, _typeRepo, _grant, _operable, NullOtRuleGate.Instance, _recorder, _publisher,
            vaultPolicy: _vault,
            mandatePolicy: _policy,
            mandateDirectory: conDirectorio ? _directory : null,
            validationPolicy: new TramiteValidationPolicy(
                TramiteValidationMode.Block, TramiteValidationMode.Block, TramiteValidationMode.Block, modo),
            logger: _log,
            personalizedDocumentResolver: _personalized);

    private void Config(string mode, string? customKind = null, Guid? otDefault = null) =>
        _policy.ResolveByOfficeIdAsync(Office, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new MandateOtConfig(
                Office, "generico", false, null, null, AssignmentMode: mode, CustomTemplateKind: customKind,
                OtDefaultMandateSignerId: otDefault));

    private void Candidates(params MandateSignerCandidate[] c) =>
        _directory.GetCandidatesAsync(Office, Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(c);

    private static MandateSignerCandidate Signer(
        string origen = MandateSignerOrigins.Organismo,
        string modelo = MandateSignerOrigins.ModeloNatural,
        string? metodo = MandateSignerOrigins.FormaBiometria,
        bool firmaValida = true,
        string? motivo = null,
        bool fisica = false) =>
        new(Guid.NewGuid(), "Firmante", DocumentoPii, null, true, FirmaValida: firmaValida, MotivoSinFirma: motivo,
            FirmaFisica: fisica, Origen: origen, SignerModel: modelo, SignatureMethod: metodo);

    private ProcedureInstance Wire(string status, bool conOrganismo = true, bool conPlaca = true, bool subsanacion = false)
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var i = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For(TramiteTipologiaCatalog.CodigoMatriculaInicial ?? "matricula_inicial"),
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000777",
            Status = status,
            SubsanacionActiva = subsanacion,
            SubsanacionCount = subsanacion ? 1 : 0,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        if (conPlaca)
        {
            i.FieldValues.Add(new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProcedureInstanceId = id,
                FieldKey = "plate", ValueText = "ABC123", Source = "consultation",
            });
        }

        if (conOrganismo)
        {
            i.FieldValues.Add(new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProcedureInstanceId = id,
                FieldKey = "transit_office_id", ValueText = Office.ToString(), Source = "user",
            });
        }

        _repo.GetByIdWithWizardGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(i);
        _typeRepo.GetByIdAsync(i.ProcedureTypeId, Arg.Any<CancellationToken>()).Returns(new ProcedureType
        {
            Id = i.ProcedureTypeId, Code = "X", Name = "X", Family = "matriculas",
            PublicationStatus = PublicationStatus.Published, WizardEnabled = true, CreatedAt = DateTimeOffset.UtcNow,
        });
        return i;
    }

    private static Task<TramiteTransitionOutcome> Radicar(
        TramiteLifecycleService sut, ProcedureInstance i, string destino = TramiteEstado.Entregado,
        string? metadata = null) =>
        sut.TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, destino, null, null, Metadata: metadata),
            TestContext.Current.CancellationToken);

    private async Task AssertDirectorioNoConsultado() =>
        await _directory.DidNotReceive().GetCandidatesAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());

    // AC1 — radica con mandatario válido.
    [Fact]
    public async Task Ac1_ConMandatarioValido_Radica_SinAvisoDelMandatario()
    {
        var i = Wire(TramiteEstado.Preparado);
        Candidates(Signer());

        var outcome = await Radicar(Sut(TramiteValidationMode.Block), i);

        outcome.Success.Should().BeTrue();
        i.Status.Should().Be(TramiteEstado.Entregado);
        _recorder.Records.Single().Metadata.Should().BeNull();
        _log.Messages.Should().NotContain(m => m.Contains("MandatarioRequerido"));
    }

    [Fact]
    public async Task Ac1_ConMandatarioValidoConBaulVigente_Radica()
    {
        var i = Wire(TramiteEstado.Preparado);
        Candidates(Signer(metodo: MandateSignerOrigins.FormaBaul));
        _vault.ResolveMandatarioAsync(Arg.Any<Guid>(), "CC", DocumentoPii, Arg.Any<CancellationToken>())
            .Returns(new SignatureVaultMatch(
                Guid.NewGuid(), "F", "h", "p", "s", DateOnly.MinValue, DateOnly.MaxValue, DocumentoPii));

        (await Radicar(Sut(TramiteValidationMode.Block), i)).Success.Should().BeTrue();
    }

    // AC2 — sin mandatario en modo block.
    [Fact]
    public async Task Ac2_SinMandatario_Block_Rechaza409ConCodigoYMensajeDeComoResolverlo()
    {
        var i = Wire(TramiteEstado.Preparado);
        Candidates();

        var outcome = await Radicar(Sut(TramiteValidationMode.Block), i);

        outcome.Success.Should().BeFalse();
        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.MandatarioNoConfigurado);
        outcome.ErrorCode.Should().Be("mandatario_no_configurado");
        outcome.ErrorDetail.Should().Contain("organismo").And.Contain("compañía").And.Contain("registre");
        i.Status.Should().Be(TramiteEstado.Preparado, "la radicación se rechazó");
        _recorder.Records.Should().BeEmpty();
        await _repo.DidNotReceive().SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ac2_Candidatos_DescartadosSoloPorVigencia_EsNoConfigurado()
    {
        var i = Wire(TramiteEstado.Preparado);
        Candidates(Signer(firmaValida: false, motivo: "mandatario_fuera_de_vigencia"));

        (await Radicar(Sut(TramiteValidationMode.Block), i)).ErrorCode
            .Should().Be(TramiteEstadoErrores.MandatarioNoConfigurado);
    }

    [Fact]
    public async Task Ac2_OtLegadoSinFilaDeConfiguracion_AplicaComoSigner_YSinMandatariosBloquea()
    {
        var i = Wire(TramiteEstado.Preparado);
        _policy.ResolveByOfficeIdAsync(Office, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((MandateOtConfig?)null);
        Candidates();

        (await Radicar(Sut(TramiteValidationMode.Block), i)).ErrorCode
            .Should().Be(TramiteEstadoErrores.MandatarioNoConfigurado);
    }

    // AC3 — persona natural sin firma válida en modo block.
    [Theory]
    [InlineData("sin_validacion_aprobada")]
    public async Task Ac3_NaturalSinFirmaValida_Block_Rechaza_ConFirmaInvalida(string motivo)
    {
        var i = Wire(TramiteEstado.Preparado);
        Candidates(Signer(firmaValida: false, motivo: motivo));

        var outcome = await Radicar(Sut(TramiteValidationMode.Block), i);

        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.MandatarioFirmaInvalida);
        outcome.ErrorCode.Should().Be("mandatario_firma_invalida");
        outcome.ErrorDetail.Should().Contain("baúl").And.Contain("validación de identidad");
    }

    [Fact]
    public async Task Ac3_NaturalConBaulSinFirmaVigenteEnElBaul_Block_Rechaza_ConFirmaInvalida()
    {
        var i = Wire(TramiteEstado.Preparado);
        Candidates(Signer(metodo: MandateSignerOrigins.FormaBaul));
        _vault.ResolveMandatarioAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((SignatureVaultMatch?)null);

        (await Radicar(Sut(TramiteValidationMode.Block), i)).ErrorCode
            .Should().Be(TramiteEstadoErrores.MandatarioFirmaInvalida);
    }

    [Fact]
    public async Task Ac3_FirmaFisicaSinMigrar_Block_NoDejaPasar_ConFirmaInvalida()
    {
        var i = Wire(TramiteEstado.Preparado);
        Candidates(Signer(firmaValida: false, motivo: "sin_validacion_aprobada", fisica: true));

        (await Radicar(Sut(TramiteValidationMode.Block), i)).ErrorCode
            .Should().Be(TramiteEstadoErrores.MandatarioFirmaInvalida);
    }

    [Fact]
    public async Task Ac3_MotivoDeFirmaGanaSobreElDeVigencia()
    {
        var i = Wire(TramiteEstado.Preparado);
        Candidates(
            Signer(MandateSignerOrigins.Organismo, firmaValida: false, motivo: "mandatario_fuera_de_vigencia"),
            Signer(MandateSignerOrigins.Compania, firmaValida: false, motivo: "sin_validacion_aprobada"));

        (await Radicar(Sut(TramiteValidationMode.Block), i)).ErrorCode
            .Should().Be(TramiteEstadoErrores.MandatarioFirmaInvalida);
    }

    // AC4 — modo warn.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ac4_Warn_Radica_YDejaAvisoEnMetadatosYLog_SinDatosPersonales(bool sinMandatarios)
    {
        var i = Wire(TramiteEstado.Preparado);
        if (sinMandatarios)
        {
            Candidates();
        }
        else
        {
            Candidates(Signer(firmaValida: false, motivo: "sin_validacion_aprobada"));
        }

        var outcome = await Radicar(Sut(TramiteValidationMode.Warn), i);

        outcome.Success.Should().BeTrue();
        i.Status.Should().Be(TramiteEstado.Entregado);
        var metadata = _recorder.Records.Single().Metadata!;
        using var doc = JsonDocument.Parse(metadata);
        var aviso = doc.RootElement.GetProperty("mandatario_aviso");
        aviso.GetProperty("codigo").GetString().Should().Be(
            sinMandatarios ? "mandatario_no_configurado" : "mandatario_firma_invalida");
        aviso.GetProperty("motivo").GetString().Should().Be(
            sinMandatarios ? "sin_mandatario_configurado" : "sin_validacion_aprobada");
        aviso.GetProperty("transitOfficeId").GetString().Should().Be(Office.ToString());
        aviso.GetProperty("companyTenantId").GetString().Should().Be(i.TenantId.ToString());
        metadata.Should().NotContain(DocumentoPii).And.NotContain("Firmante");

        _log.Messages.Should().ContainSingle(m => m.Contains("MandatarioRequerido"))
            .Which.Should().Contain(Office.ToString()).And.NotContain(DocumentoPii).And.NotContain("Firmante");
    }

    [Fact]
    public async Task Ac4_Warn_ConservaLosMetadatosQueYaTraiaLaTransicion()
    {
        var i = Wire(TramiteEstado.Preparado);
        Candidates();

        await Radicar(Sut(TramiteValidationMode.Warn), i, metadata: "{\"origen\":\"x\"}");

        using var doc = JsonDocument.Parse(_recorder.Records.Single().Metadata!);
        doc.RootElement.GetProperty("origen").GetString().Should().Be("x");
        doc.RootElement.TryGetProperty("mandatario_aviso", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Ac4_Warn_ConMandatarioValido_NoDejaAviso()
    {
        var i = Wire(TramiteEstado.Preparado);
        Candidates(Signer());

        await Radicar(Sut(TramiteValidationMode.Warn), i);

        _recorder.Records.Single().Metadata.Should().BeNull();
    }

    [Fact]
    public async Task Ac4_Warn_SiElDirectorioFalla_LaRadicacionContinua()
    {
        var i = Wire(TramiteEstado.Preparado);
        _directory.GetCandidatesAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<MandateSignerCandidate>>(_ => throw new InvalidOperationException("db"));

        var outcome = await Radicar(Sut(TramiteValidationMode.Warn), i);

        outcome.Success.Should().BeTrue();
        _recorder.Records.Single().Metadata.Should().BeNull();
    }

    // AC5 — modo off.
    [Fact]
    public async Task Ac5_Off_NoConsultaElDirectorio_NiRegistraAviso()
    {
        var i = Wire(TramiteEstado.Preparado);
        Candidates();

        var outcome = await Radicar(Sut(TramiteValidationMode.Off), i);

        outcome.Success.Should().BeTrue();
        await AssertDirectorioNoConsultado();
        _recorder.Records.Single().Metadata.Should().BeNull();
        _log.Messages.Should().NotContain(m => m.Contains("MandatarioRequerido"));
    }

    [Fact]
    public async Task Ac5_SinDirectorioCableado_NoSeEvalua_AunEnBlock()
    {
        var i = Wire(TramiteEstado.Preparado);

        var outcome = await Radicar(Sut(TramiteValidationMode.Block, conDirectorio: false), i);

        outcome.Success.Should().BeTrue();
        await AssertDirectorioNoConsultado();
    }

    // AC6 — exclusiones.
    [Theory]
    [InlineData("institutional")]
    [InlineData("open")]
    public async Task Ac6_PersonaJuridicaOMandatoAbierto_NoExigeMandatario_EnBlock(string modo)
    {
        var i = Wire(TramiteEstado.Preparado);
        Config(modo);
        Candidates();

        var outcome = await Radicar(Sut(TramiteValidationMode.Block), i);

        outcome.Success.Should().BeTrue();
        await AssertDirectorioNoConsultado();
    }

    [Fact]
    public async Task Ac6_MandatoPersonalizadoDeLaCompania_NoExigeMandatario_EnBlock()
    {
        var i = Wire(TramiteEstado.Preparado);
        Candidates();
        _personalized.ResolveAsync(Arg.Any<Guid>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new PersonalizedDocumentResolution(
                [new ResolvedPersonalizedDocument("mandato", "m.pdf", [1], Guid.NewGuid(), 1, "sha", 1)], []));

        var outcome = await Radicar(Sut(TramiteValidationMode.Block), i);

        outcome.Success.Should().BeTrue();
        await AssertDirectorioNoConsultado();
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("editor")]
    public async Task Ac6_PlantillaPropiaDelOt_SiAplica_PorqueSuPdfEstampaLasFirmas(string kind)
    {
        var i = Wire(TramiteEstado.Preparado);
        Config("signer", customKind: kind);
        Candidates();

        (await Radicar(Sut(TramiteValidationMode.Block), i)).ErrorCode
            .Should().Be(TramiteEstadoErrores.MandatarioNoConfigurado);
    }

    [Theory]
    [InlineData(MandateSignerOrigins.ModeloJuridica)]
    [InlineData(MandateSignerOrigins.ModeloFormatoBlanco)]
    public async Task Ac6_JuridicaYFormatoEnBlanco_NoExigenFormaDeFirma_Radican(string modelo)
    {
        var i = Wire(TramiteEstado.Preparado);
        Candidates(Signer(modelo: modelo, metodo: null));

        (await Radicar(Sut(TramiteValidationMode.Block), i)).Success.Should().BeTrue();
    }

    // AC7 — default del OT.
    [Fact]
    public async Task Ac7_DefaultDelOtNoVinculado_SeValidaSuVigenciaYFirma_Vigente_Radica()
    {
        var i = Wire(TramiteEstado.Preparado);
        var defaultOt = Signer();
        Config("signer", otDefault: defaultOt.Id);
        Candidates();
        _directory.GetByIdAsync(defaultOt.Id, Arg.Any<CancellationToken>()).Returns(defaultOt);

        (await Radicar(Sut(TramiteValidationMode.Block), i)).Success.Should().BeTrue();
    }

    [Fact]
    public async Task Ac7_DefaultDelOtNoVinculado_SinFirmaValida_Bloquea()
    {
        var i = Wire(TramiteEstado.Preparado);
        var defaultOt = Signer(firmaValida: false, motivo: "sin_validacion_aprobada");
        Config("signer", otDefault: defaultOt.Id);
        Candidates();
        _directory.GetByIdAsync(defaultOt.Id, Arg.Any<CancellationToken>()).Returns(defaultOt);

        (await Radicar(Sut(TramiteValidationMode.Block), i)).ErrorCode
            .Should().Be(TramiteEstadoErrores.MandatarioFirmaInvalida);
    }

    // AC8 — subsanación, organismo sin elegir y destinos de /submit.
    [Fact]
    public async Task Ac8_ReRadicacionDesdeSubsanacion_SeEvaluaIgualQueLaPrimera()
    {
        var i = Wire(TramiteEstado.Rechazado, subsanacion: true);
        Candidates();

        (await Radicar(Sut(TramiteValidationMode.Block), i)).ErrorCode
            .Should().Be(TramiteEstadoErrores.MandatarioNoConfigurado);
        await _directory.Received(1).GetCandidatesAsync(
            Office, i.TenantId, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ac8_ReRadicacionDesdeSubsanacion_ConMandatario_Radica()
    {
        var i = Wire(TramiteEstado.Rechazado, subsanacion: true);
        Candidates(Signer());

        (await Radicar(Sut(TramiteValidationMode.Block), i)).Success.Should().BeTrue();
    }

    [Fact]
    public async Task Ac8_DestinoPreasignacion_TambienPasaPorElGate()
    {
        var i = Wire(TramiteEstado.Preparado, conPlaca: false);
        Candidates();

        (await Radicar(Sut(TramiteValidationMode.Block), i, TramiteEstado.Preasignacion)).ErrorCode
            .Should().Be(TramiteEstadoErrores.MandatarioNoConfigurado);
    }

    [Fact]
    public async Task Ac8_DestinoPreasignacion_ConMandatario_Radica()
    {
        var i = Wire(TramiteEstado.Preparado, conPlaca: false);
        Candidates(Signer());

        (await Radicar(Sut(TramiteValidationMode.Block), i, TramiteEstado.Preasignacion)).Success.Should().BeTrue();
    }

    [Fact]
    public async Task Ac8_SinOrganismoElegido_NoBloquea_NiConsultaElDirectorio()
    {
        var i = Wire(TramiteEstado.Preparado, conOrganismo: false);
        Candidates();

        var outcome = await Radicar(Sut(TramiteValidationMode.Block), i);

        outcome.Success.Should().BeTrue();
        await AssertDirectorioNoConsultado();
    }

    // Orden de gates y alcance.
    [Fact]
    public async Task Orden_ElGateDeGrantCortaAntes_YNoSeConsultaElDirectorio()
    {
        var i = Wire(TramiteEstado.Preparado);
        _grant.IsEnabledForTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        Candidates();

        (await Radicar(Sut(TramiteValidationMode.Block), i)).ErrorCode
            .Should().Be(TramiteEstadoErrores.OrganismoNoHabilitado);
        await AssertDirectorioNoConsultado();
    }

    [Fact]
    public async Task Alcance_ElGateNoCorreEnTransicionesQueNoSonRadicacion()
    {
        // Borrador → anulado no es una radicación: el mandatario no se evalúa.
        var i = Wire(TramiteEstado.Borrador);
        Candidates();

        var outcome = await Sut(TramiteValidationMode.Block).TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, TramiteEstado.Anulado, "motivo", null),
            TestContext.Current.CancellationToken);

        outcome.Success.Should().BeTrue();
        await AssertDirectorioNoConsultado();
    }

    private sealed class CapturingLogger : ILogger<TramiteLifecycleService>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
