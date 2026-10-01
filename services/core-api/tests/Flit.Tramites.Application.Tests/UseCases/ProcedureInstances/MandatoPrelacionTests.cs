using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #13142 (ADR-0066) — la aprobación resuelve el firmante con la prelación única: el OT prevalece sobre la
/// compañía, el baúl se consulta de verdad, el default del OT entra sin vínculo y el cotejo por usuario solo
/// desempata un nivel ambiguo.
/// </summary>
public sealed class MandatoPrelacionTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IMandateSignerDirectory _directory = Substitute.For<IMandateSignerDirectory>();
    private readonly ISignatureVaultPolicy _vault = Substitute.For<ISignatureVaultPolicy>();
    private readonly IMandateRequirementPolicy _policy = Substitute.For<IMandateRequirementPolicy>();

    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Office = Guid.NewGuid();

    private MandatoApprovalHandler Handler() => new(_repo, _directory, _vault, _policy);

    private ProcedureInstance Seed(Guid? saved = null)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            TransitOfficeId = Office,
            MandateSignerId = saved,
            Attachments = [new ProcedureInstanceAttachment { Tipo = "mandato", Source = "system" }],
            FieldValues = [new ProcedureInstanceFieldValue { FieldKey = "transit_office_code", ValueText = "05001" }],
        };
        _repo.GetByIdWithFurGraphAsync(instance.Id, Tenant, Arg.Any<CancellationToken>()).Returns(instance);
        return instance;
    }

    private void Config(Guid? otDefault = null, Guid? designado = null, string mode = "signer") =>
        _policy.ResolveAsync("05001", Tenant, Arg.Any<CancellationToken>())
            .Returns(new MandateOtConfig(
                Office, "generico", false, null, null, AssignmentMode: mode,
                OtDefaultMandateSignerId: otDefault, DefaultMandateSignerId: designado));

    private void Candidates(params MandateSignerCandidate[] c) =>
        _directory.GetCandidatesAsync(Office, Tenant, Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(c);

    private static MandateSignerCandidate Signer(
        string origen = MandateSignerOrigins.Organismo,
        Guid? userId = null,
        string? metodo = MandateSignerOrigins.FormaBiometria,
        bool firmaValida = true,
        string? motivo = null,
        bool fisica = false) =>
        new(Guid.NewGuid(), "Firmante", "123", userId, true, FirmaValida: firmaValida, MotivoSinFirma: motivo,
            FirmaFisica: fisica, Origen: origen, SignatureMethod: metodo);

    private Task<MandatoApprovalDecision> Check(ProcedureInstance i, Guid? user = null, Guid? explicitId = null) =>
        Handler().CheckAsync(i.Id, Tenant, user, explicitId, TestContext.Current.CancellationToken);

    [Fact]
    public async Task ElOt_PrevaleceSobreLaCompania_AunqueElAprobadorSeaElMandatarioDeLaCompania()
    {
        var instance = Seed();
        Config();
        var user = Guid.NewGuid();
        var ot = Signer(MandateSignerOrigins.Organismo);
        var compania = Signer(MandateSignerOrigins.Compania, userId: user);
        Candidates(compania, ot);

        var decision = await Check(instance, user);

        decision.Outcome.Should().Be(MandatoApprovalOutcome.Resolved);
        decision.MandateSignerId.Should().Be(ot.Id, "el cotejo por usuario ya no manda: solo desempata");
    }

    [Fact]
    public async Task NivelAmbiguo_ElCotejoPorUsuarioDesempata()
    {
        var instance = Seed();
        Config();
        var user = Guid.NewGuid();
        var a = Signer(userId: Guid.NewGuid());
        var b = Signer(userId: user);
        Candidates(a, b);

        var decision = await Check(instance, user);

        decision.Outcome.Should().Be(MandatoApprovalOutcome.Resolved);
        decision.MandateSignerId.Should().Be(b.Id);
    }

    [Fact]
    public async Task NivelAmbiguo_SinCotejo_ExigeElegir()
    {
        var instance = Seed();
        Config();
        Candidates(Signer(), Signer());

        (await Check(instance, Guid.NewGuid())).Outcome.Should().Be(MandatoApprovalOutcome.RequiereSeleccion);
    }

    [Fact]
    public async Task EleccionDelOtQueNoEsValida_ExigeElegirOtraVez_YElGuardadoInvalidoSeIgnora()
    {
        var instance = Seed(saved: Guid.NewGuid());
        Config();
        var unico = Signer();
        Candidates(unico);

        // Guardado que ya no es candidato: se ignora y se resuelve por prelación.
        (await Check(instance)).MandateSignerId.Should().Be(unico.Id);
        // Elección explícita del OT fuera de los válidos: 409.
        (await Check(instance, explicitId: Guid.NewGuid())).Outcome.Should().Be(MandatoApprovalOutcome.RequiereSeleccion);
    }

    [Fact]
    public async Task DefaultDelOt_SinVinculoConLaCompania_SeResuelve_SiEstaVigente()
    {
        var instance = Seed();
        var defaultOt = Signer();
        Config(otDefault: defaultOt.Id);
        Candidates();
        _directory.GetByIdAsync(defaultOt.Id, Arg.Any<CancellationToken>()).Returns(defaultOt);

        var decision = await Check(instance);

        decision.Outcome.Should().Be(MandatoApprovalOutcome.Resolved);
        decision.MandateSignerId.Should().Be(defaultOt.Id);
    }

    [Fact]
    public async Task DefaultDelOt_ConBajaLogica_NoSeResuelve_YElOtDebeElegir()
    {
        var instance = Seed();
        var defaultOt = Signer();
        Config(otDefault: defaultOt.Id);
        Candidates();
        // El directorio ya no devuelve a un eliminado (AC4): GetByIdAsync devuelve null.
        _directory.GetByIdAsync(defaultOt.Id, Arg.Any<CancellationToken>()).Returns((MandateSignerCandidate?)null);

        // HU #13147b — nadie resuelve y el trámite exige firmante: 409 (el OT elige o registra uno).
        (await Check(instance)).Outcome.Should().Be(MandatoApprovalOutcome.RequiereSeleccion);
    }

    [Fact]
    public async Task MandatarioDelOtVencido_SeSaltaAlDeLaCompania()
    {
        var instance = Seed();
        Config();
        var vencido = Signer(MandateSignerOrigins.Organismo, firmaValida: false, motivo: "mandatario_fuera_de_vigencia");
        var compania = Signer(MandateSignerOrigins.Compania);
        Candidates(vencido, compania);

        (await Check(instance)).MandateSignerId.Should().Be(compania.Id);
    }

    [Fact]
    public async Task Baul_SeConsultaDeVerdad_SinFirmaVigenteSeDescarta_ConFirmaVigenteCuenta()
    {
        var instance = Seed();
        Config();
        var conBaul = Signer(MandateSignerOrigins.Organismo, metodo: MandateSignerOrigins.FormaBaul);
        Candidates(conBaul);

        // Sin firma vigente en el baúl: descartado, no hay firmante: el OT debe elegir (409, HU #13147b).
        _vault.ResolveMandatarioAsync(Tenant, "CC", "123", Arg.Any<CancellationToken>())
            .Returns((SignatureVaultMatch?)null);
        (await Check(instance)).Outcome.Should().Be(MandatoApprovalOutcome.RequiereSeleccion);

        _vault.ResolveMandatarioAsync(Tenant, "CC", "123", Arg.Any<CancellationToken>())
            .Returns(new SignatureVaultMatch(
                Guid.NewGuid(), "Firmante", "h", "p", "s", DateOnly.MinValue, DateOnly.MaxValue, "123"));
        (await Check(instance)).MandateSignerId.Should().Be(conBaul.Id);
    }

    [Theory]
    [InlineData("institutional")]
    [InlineData("open")]
    public async Task ModosSinFirmantePersona_NoResuelvenNiConsultanElDirectorio(string mode)
    {
        var instance = Seed();
        Config(mode: mode);

        (await Check(instance)).Outcome.Should().Be(MandatoApprovalOutcome.NotApplicable);
        await _directory.DidNotReceive().GetCandidatesAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FirmaFisicaSinMigrar_SeSigueHonrandoEnLaAprobacion()
    {
        var instance = Seed();
        Config();
        var fisico = Signer(firmaValida: false, motivo: "sin_validacion_aprobada", fisica: true);
        Candidates(fisico);

        var decision = await Check(instance);

        decision.Outcome.Should().Be(MandatoApprovalOutcome.Resolved);
        decision.MandateSignerId.Should().Be(fisico.Id);
    }
}
