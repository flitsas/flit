using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>HU #13144 (ADR-0066) — evaluador único del mandatario: estados, motivos y exclusiones.</summary>
public sealed class MandateSignerEvaluatorTests
{
    private static readonly Guid Office = Guid.NewGuid();

    private readonly IMandateSignerDirectory _directory = Substitute.For<IMandateSignerDirectory>();
    private readonly IMandateRequirementPolicy _policy = Substitute.For<IMandateRequirementPolicy>();
    private readonly ISignatureVaultPolicy _vault = Substitute.For<ISignatureVaultPolicy>();
    private readonly IPersonalizedDocumentResolver _personalized = Substitute.For<IPersonalizedDocumentResolver>();

    public MandateSignerEvaluatorTests()
    {
        _personalized.ResolveAsync(Arg.Any<Guid>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(PersonalizedDocumentResolution.Empty);
    }

    private MandateSignerEvaluator Sut() => new(_directory, _policy, _vault, _personalized);

    private static MandateSignerCandidate Signer(
        string origen = MandateSignerOrigins.Organismo,
        string modelo = MandateSignerOrigins.ModeloNatural,
        string? metodo = MandateSignerOrigins.FormaBiometria,
        bool firmaValida = true,
        string? motivo = null) =>
        new(Guid.NewGuid(), "Firmante", "123", null, true, FirmaValida: firmaValida, MotivoSinFirma: motivo,
            Origen: origen, SignerModel: modelo, SignatureMethod: metodo);

    private static ProcedureInstance Instance(bool conOrganismo = true)
    {
        var i = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Status = TramiteEstado.Preparado,
        };
        if (conOrganismo)
        {
            i.FieldValues.Add(new ProcedureInstanceFieldValue
            {
                FieldKey = "transit_office_id",
                ValueText = Office.ToString(),
            });
        }

        return i;
    }

    private void Config(string mode = "signer") =>
        _policy.ResolveByOfficeIdAsync(Office, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new MandateOtConfig(Office, "generico", false, null, null, AssignmentMode: mode));

    private void Candidates(params MandateSignerCandidate[] c) =>
        _directory.GetCandidatesAsync(Office, Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(c);

    private Task<MandateSignerEvaluacion> Evaluate(ProcedureInstance i, Guid? eleccion = null) =>
        Sut().EvaluateAsync(i, eleccion, TestContext.Current.CancellationToken);

    [Fact]
    public async Task SinOrganismo_PendienteOrganismo_SinConsultarElDirectorio()
    {
        var r = await Evaluate(Instance(conOrganismo: false));

        r.Estado.Should().Be(MandateSignerEstado.PendienteOrganismo);
        r.CodigoDeError.Should().BeNull();
        await _directory.DidNotReceive().GetCandidatesAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("institutional")]
    [InlineData("open")]
    public async Task ModosSinFirmantePersona_NoAplica(string modo)
    {
        Config(modo);

        var r = await Evaluate(Instance());

        r.Estado.Should().Be(MandateSignerEstado.NoAplica);
        r.CodigoDeError.Should().BeNull();
    }

    [Fact]
    public async Task MandatoPersonalizadoDeCompania_NoAplica()
    {
        Config();
        _personalized.ResolveAsync(Arg.Any<Guid>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new PersonalizedDocumentResolution(
                [new ResolvedPersonalizedDocument("mandato", "m.pdf", [1], Guid.NewGuid(), 1, "s", 1)], []));

        (await Evaluate(Instance())).Estado.Should().Be(MandateSignerEstado.NoAplica);
    }

    [Fact]
    public async Task ConMandatarioValido_Valido_ConNivelYFormaDeFirma()
    {
        Config();
        var ot = Signer(MandateSignerOrigins.Organismo, metodo: MandateSignerOrigins.FormaBiometria);
        Candidates(Signer(MandateSignerOrigins.Compania), ot);

        var r = await Evaluate(Instance());

        r.Estado.Should().Be(MandateSignerEstado.Valido);
        r.Nivel.Should().Be(MandateSignerLevel.OtParaCompania);
        r.Signer!.Id.Should().Be(ot.Id);
        r.FormaFirma.Should().Be("biometria");
        r.Motivo.Should().BeNull();
    }

    [Fact]
    public async Task Juridica_FormaDeFirmaNula()
    {
        Config();
        Candidates(Signer(modelo: MandateSignerOrigins.ModeloJuridica, metodo: null));

        var r = await Evaluate(Instance());

        r.Estado.Should().Be(MandateSignerEstado.Valido);
        r.FormaFirma.Should().BeNull();
    }

    [Fact]
    public async Task VariosValidosSinDesempate_PendienteEleccionOt_SinCodigoDeError()
    {
        Config();
        Candidates(Signer(), Signer());

        var r = await Evaluate(Instance());

        r.Estado.Should().Be(MandateSignerEstado.PendienteEleccionOt);
        r.CodigoDeError.Should().BeNull();
        r.Candidatos.Should().HaveCount(2);
    }

    [Fact]
    public async Task SinCandidatos_SinMandatario_ConMotivoSinMandatarioConfigurado()
    {
        Config();
        Candidates();

        var r = await Evaluate(Instance());

        r.Estado.Should().Be(MandateSignerEstado.SinMandatario);
        r.Motivo.Should().Be("sin_mandatario_configurado");
        r.CodigoDeError.Should().Be("mandatario_no_configurado");
    }

    [Fact]
    public async Task DescartadosPorVigencia_SinMandatario_ConElMotivoDelDescarte()
    {
        Config();
        Candidates(Signer(firmaValida: false, motivo: "mandatario_fuera_de_vigencia"));

        var r = await Evaluate(Instance());

        r.Estado.Should().Be(MandateSignerEstado.SinMandatario);
        r.Motivo.Should().Be("mandatario_fuera_de_vigencia");
    }

    [Fact]
    public async Task DescartadosPorFirma_FirmaInvalida_ConCodigoYMotivo()
    {
        Config();
        Candidates(Signer(firmaValida: false, motivo: "sin_validacion_aprobada"));

        var r = await Evaluate(Instance());

        r.Estado.Should().Be(MandateSignerEstado.FirmaInvalida);
        r.Motivo.Should().Be("sin_validacion_aprobada");
        r.CodigoDeError.Should().Be("mandatario_firma_invalida");
        r.MensajeDeError.Should().Contain("baúl").And.Contain("validación de identidad");
    }

    [Fact]
    public async Task EleccionDelOtInvalida_ConValidosDisponibles_PendienteEleccionOt()
    {
        Config();
        Candidates(Signer());

        var r = await Evaluate(Instance(), eleccion: Guid.NewGuid());

        r.Estado.Should().Be(MandateSignerEstado.PendienteEleccionOt);
        r.Candidatos.Should().HaveCount(1);
    }

    [Fact]
    public async Task EnBorradorElGuardadoSeIgnora_FueraDeBorradorManda()
    {
        Config();
        var a = Signer(MandateSignerOrigins.Organismo);
        var b = Signer(MandateSignerOrigins.Compania);
        Candidates(a, b);

        var preparado = Instance();
        preparado.MandateSignerId = b.Id;
        (await Evaluate(preparado)).Signer!.Id.Should().Be(b.Id, "fuera de borrador manda el guardado válido");

        var borrador = Instance();
        borrador.Status = TramiteEstado.Borrador;
        borrador.MandateSignerId = b.Id;
        (await Evaluate(borrador)).Signer!.Id.Should().Be(a.Id, "en borrador se recalcula por prelación");
    }

    [Fact]
    public async Task TramiteAprobado_ConservaAlFirmanteGuardado_AunqueDespuesVenza()
    {
        // Hallazgo de la validación de la épica #13090: tras aprobar, si el mandatario vencía el indicador pasaba a
        // otra persona mientras el PDF ya emitido (que no se regenera) seguía diciendo la original.
        Config();
        var firmante = Signer(MandateSignerOrigins.Organismo);
        var otro = Signer(MandateSignerOrigins.Compania);
        var vencido = firmante with { FirmaValida = false, MotivoSinFirma = "mandatario_fuera_de_vigencia" };
        Candidates(vencido, otro);

        var aprobado = Instance();
        aprobado.Status = TramiteEstado.Aprobado;
        aprobado.MandateSignerId = firmante.Id;

        var r = await Evaluate(aprobado);

        r.Estado.Should().Be(MandateSignerEstado.Valido);
        r.Signer!.Id.Should().Be(firmante.Id, "el mandato aprobado ya salió con ese firmante");
        r.Nivel.Should().Be(MandateSignerLevel.Explicita);
    }

    [Fact]
    public async Task TramiteRadicadoSinAprobar_SiElGuardadoVencio_SeRecalculaYElOtroEntra()
    {
        // Antes de aprobar sí debe cambiar: el OT elegirá con el candidato válido que quede.
        Config();
        var firmante = Signer(MandateSignerOrigins.Organismo);
        var otro = Signer(MandateSignerOrigins.Compania);
        var vencido = firmante with { FirmaValida = false, MotivoSinFirma = "mandatario_fuera_de_vigencia" };
        Candidates(vencido, otro);

        var entregado = Instance();
        entregado.Status = TramiteEstado.Entregado;
        entregado.MandateSignerId = firmante.Id;

        (await Evaluate(entregado)).Signer!.Id.Should().Be(otro.Id);
    }

    [Fact]
    public void Clasificar_ElEstadoSeTraduceAlVocabularioEstable()
    {
        MandateSignerEstados.ToCode(MandateSignerEstado.Valido).Should().Be("valido");
        MandateSignerEstados.ToCode(MandateSignerEstado.SinMandatario).Should().Be("sin_mandatario");
        MandateSignerEstados.ToCode(MandateSignerEstado.FirmaInvalida).Should().Be("firma_invalida");
        MandateSignerEstados.ToCode(MandateSignerEstado.NoAplica).Should().Be("no_aplica");
        MandateSignerEstados.ToCode(MandateSignerEstado.PendienteOrganismo).Should().Be("pendiente_organismo");
        MandateSignerEstados.ToCode(MandateSignerEstado.PendienteEleccionOt).Should().Be("pendiente_eleccion_ot");
    }
}
