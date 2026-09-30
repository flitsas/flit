using System.Text.Json;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #13145 (ADR-0066) — firmante previsto de solo lectura y candidatos reales del 409 de la aprobación.
/// Reutilizan el evaluador de #13144 y nunca exponen documento de identidad ni ruta de firma.
/// </summary>
public sealed class MandateSignerPrevistoTests
{
    private static readonly Guid Office = Guid.NewGuid();
    private static readonly Guid Tenant = Guid.NewGuid();
    private const string DocumentoPii = "9988776655";

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IMandateSignerDirectory _directory = Substitute.For<IMandateSignerDirectory>();
    private readonly IMandateRequirementPolicy _policy = Substitute.For<IMandateRequirementPolicy>();
    private readonly IPersonalizedDocumentResolver _personalized = Substitute.For<IPersonalizedDocumentResolver>();

    public MandateSignerPrevistoTests()
    {
        _personalized.ResolveAsync(Arg.Any<Guid>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(PersonalizedDocumentResolution.Empty);
        Config("signer");
    }

    private GetMandateSignerPrevistoHandler Sut(TramiteValidationMode modo = TramiteValidationMode.Warn) =>
        new(_repo,
            new MandateSignerEvaluator(_directory, _policy, null, _personalized),
            new TramiteValidationPolicy(
                TramiteValidationMode.Block, TramiteValidationMode.Block, TramiteValidationMode.Block, modo));

    private void Config(string mode) =>
        _policy.ResolveByOfficeIdAsync(Office, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new MandateOtConfig(Office, "generico", false, null, null, AssignmentMode: mode));

    private void Candidates(params MandateSignerCandidate[] c) =>
        _directory.GetCandidatesAsync(Office, Tenant, Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(c);

    private static MandateSignerCandidate Signer(
        string nombre = "Ana Restrepo",
        string origen = MandateSignerOrigins.Organismo,
        string? metodo = MandateSignerOrigins.FormaBiometria,
        bool firmaValida = true,
        string? motivo = null) =>
        new(Guid.NewGuid(), nombre, DocumentoPii, null, true, SignatureVaultId: Guid.NewGuid(),
            FirmaValida: firmaValida, MotivoSinFirma: motivo,
            Origen: origen, SignerModel: MandateSignerOrigins.ModeloNatural, SignatureMethod: metodo);

    private ProcedureInstance Seed(bool conOrganismo = true)
    {
        var i = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            Status = TramiteEstado.Borrador,
        };
        if (conOrganismo)
        {
            i.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "transit_office_id", ValueText = Office.ToString() });
        }

        _repo.GetByIdWithDetailsAsync(i.Id, Tenant, Arg.Any<CancellationToken>()).Returns(i);
        return i;
    }

    private async Task<MandateSignerPrevistoDto> Consultar(ProcedureInstance i, TramiteValidationMode modo = TramiteValidationMode.Warn)
    {
        var (result, error) = await Sut(modo).HandleAsync(i.Id, Tenant, TestContext.Current.CancellationToken);
        error.Should().BeNull();
        return result!;
    }

    // AC1 — firmante previsto.
    [Theory]
    [InlineData(MandateSignerOrigins.FormaBiometria)]
    [InlineData(MandateSignerOrigins.FormaBaul)]
    public async Task Ac1_ConMandatarioValido_DevuelveEstadoNombreFormaDeFirmaYModo(string forma)
    {
        var i = Seed();
        Candidates(Signer(metodo: forma));
        // El baúl no está cableado en este sut: con forma baúl el firmante quedaría descartado, así que se usa
        // un evaluador con baúl vigente.
        var vault = Substitute.For<ISignatureVaultPolicy>();
        vault.ResolveMandatarioAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SignatureVaultMatch(
                Guid.NewGuid(), "F", "h", "p", "s", DateOnly.MinValue, DateOnly.MaxValue, DocumentoPii));
        var sut = new GetMandateSignerPrevistoHandler(
            _repo, new MandateSignerEvaluator(_directory, _policy, vault, _personalized),
            new TramiteValidationPolicy(
                TramiteValidationMode.Block, TramiteValidationMode.Block, TramiteValidationMode.Block,
                TramiteValidationMode.Warn));

        var (dto, error) = await sut.HandleAsync(i.Id, Tenant, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        dto!.Estado.Should().Be("valido");
        dto.Nombre.Should().Be("Ana Restrepo");
        dto.FormaFirma.Should().Be(forma);
        dto.Modo.Should().Be("warn");
        dto.Motivo.Should().BeNull();
    }

    [Theory]
    [InlineData(TramiteValidationMode.Block, "block")]
    [InlineData(TramiteValidationMode.Warn, "warn")]
    [InlineData(TramiteValidationMode.Off, "off")]
    public async Task Ac1_ElModoVigenteDeLaValidacionViajaEnLaRespuesta(TramiteValidationMode modo, string esperado)
    {
        var i = Seed();
        Candidates(Signer());

        (await Consultar(i, modo)).Modo.Should().Be(esperado);
    }

    // AC2 — sin mandatario.
    [Fact]
    public async Task Ac2_SinMandatario_DevuelveSinMandatarioConMotivoYModo()
    {
        var i = Seed();
        Candidates();

        var dto = await Consultar(i, TramiteValidationMode.Block);

        dto.Estado.Should().Be("sin_mandatario");
        dto.Motivo.Should().Be("sin_mandatario_configurado");
        dto.Modo.Should().Be("block");
        dto.Nombre.Should().BeNull();
        dto.FormaFirma.Should().BeNull();
    }

    [Fact]
    public async Task Ac2_SinFirmaValida_DevuelveFirmaInvalidaConMotivo_SinNombre()
    {
        var i = Seed();
        Candidates(Signer(firmaValida: false, motivo: "biometria_vencida"));

        var dto = await Consultar(i);

        dto.Estado.Should().Be("firma_invalida");
        dto.Motivo.Should().Be("biometria_vencida");
        dto.Nombre.Should().BeNull("solo el firmante válido se nombra");
        dto.Modo.Should().Be("warn");
    }

    // AC3 — no aplica o sin organismo.
    [Theory]
    [InlineData("institutional")]
    [InlineData("open")]
    public async Task Ac3_MandatoInstitucionalOAbierto_NoAplica(string modo)
    {
        var i = Seed();
        Config(modo);

        var dto = await Consultar(i);

        dto.Estado.Should().Be("no_aplica");
        dto.Nombre.Should().BeNull();
    }

    [Fact]
    public async Task Ac3_MandatoPersonalizado_NoAplica()
    {
        var i = Seed();
        _personalized.ResolveAsync(Arg.Any<Guid>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new PersonalizedDocumentResolution(
                [new ResolvedPersonalizedDocument("mandato", "m.pdf", [1], Guid.NewGuid(), 1, "s", 1)], []));

        (await Consultar(i)).Estado.Should().Be("no_aplica");
    }

    [Fact]
    public async Task Ac3_SinOrganismo_PendienteOrganismo_SinConsultarElDirectorio()
    {
        var i = Seed(conOrganismo: false);

        var dto = await Consultar(i);

        dto.Estado.Should().Be("pendiente_organismo");
        await _directory.DidNotReceive().GetCandidatesAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ac3_VariosValidosSinDesempate_PendienteEleccionOt_SinNombre()
    {
        var i = Seed();
        Candidates(Signer("Ana"), Signer("Beto"));

        var dto = await Consultar(i);

        dto.Estado.Should().Be("pendiente_eleccion_ot");
        dto.Nombre.Should().BeNull();
    }

    // AC5 — aislamiento y privacidad.
    [Fact]
    public async Task Ac5_TramiteDeOtroTenant_NotFound()
    {
        var i = Seed();
        var otroTenant = Guid.NewGuid();

        var (result, error) = await Sut().HandleAsync(i.Id, otroTenant, TestContext.Current.CancellationToken);

        result.Should().BeNull();
        error.Should().Be("not_found");
        await _directory.DidNotReceive().GetCandidatesAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ac5_LaRespuestaNuncaIncluyeDocumentoNiRutaDeFirma()
    {
        var i = Seed();
        Candidates(Signer());

        var json = JsonSerializer.Serialize(await Consultar(i));

        json.Should().NotContain(DocumentoPii)
            .And.NotContainEquivalentOf("documento")
            .And.NotContainEquivalentOf("storage")
            .And.NotContainEquivalentOf("signatureVault")
            .And.NotContainEquivalentOf("ruta");
    }

    // AC6 — candidatos del 409 de la aprobación.
    [Fact]
    public async Task Ac6_ElOtApruebaSinElegir_ElConflictoIncluyeLosCandidatosVigentesDelBackend()
    {
        var office = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = Guid.NewGuid(),
            TenantId = tenant,
            TransitOfficeId = office,
            Attachments = [new ProcedureInstanceAttachment { Tipo = "mandato", Source = "system" }],
        };
        var repo = Substitute.For<IProcedureInstanceRepository>();
        repo.GetByIdWithFurGraphAsync(instance.Id, tenant, Arg.Any<CancellationToken>()).Returns(instance);
        var directory = Substitute.For<IMandateSignerDirectory>();
        var ana = Signer("Ana Restrepo", metodo: MandateSignerOrigins.FormaBiometria);
        var beto = Signer("Beto Pérez", metodo: MandateSignerOrigins.FormaBiometria);
        var vencido = Signer("Carla Vencida", firmaValida: false, motivo: "mandatario_fuera_de_vigencia");
        directory.GetCandidatesAsync(office, tenant, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([ana, beto, vencido]);

        var decision = await new MandatoApprovalHandler(repo, directory)
            .CheckAsync(instance.Id, tenant, Guid.NewGuid(), null, TestContext.Current.CancellationToken);

        decision.Outcome.Should().Be(MandatoApprovalOutcome.RequiereSeleccion);
        decision.Candidatos.Should().NotBeNull();
        decision.Candidatos!.Select(c => c.Id).Should().BeEquivalentTo([ana.Id, beto.Id], "el vencido no es candidato");
        decision.Candidatos.Should().OnlyContain(c => c.FormaFirma == "biometria" && c.Nombre.Length > 0);

        var json = JsonSerializer.Serialize(decision.Candidatos);
        json.Should().NotContain(DocumentoPii).And.NotContainEquivalentOf("storage");
    }

    [Fact]
    public async Task Ac6_EleccionInvalida_LosCandidatosSonLosValidos_YNoHayCandidatosSiNoHayValidos()
    {
        var office = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = Guid.NewGuid(),
            TenantId = tenant,
            TransitOfficeId = office,
            Attachments = [new ProcedureInstanceAttachment { Tipo = "mandato", Source = "system" }],
        };
        var repo = Substitute.For<IProcedureInstanceRepository>();
        repo.GetByIdWithFurGraphAsync(instance.Id, tenant, Arg.Any<CancellationToken>()).Returns(instance);
        var directory = Substitute.For<IMandateSignerDirectory>();
        directory.GetCandidatesAsync(office, tenant, Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns([]);

        var decision = await new MandatoApprovalHandler(repo, directory)
            .CheckAsync(instance.Id, tenant, null, Guid.NewGuid(), TestContext.Current.CancellationToken);

        decision.Outcome.Should().Be(MandatoApprovalOutcome.RequiereSeleccion);
        decision.Candidatos.Should().BeEmpty();
    }

    [Fact]
    public async Task Ac6_ConUnResuelto_NoHayCandidatosEnLaDecision()
    {
        var office = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = Guid.NewGuid(),
            TenantId = tenant,
            TransitOfficeId = office,
            Attachments = [new ProcedureInstanceAttachment { Tipo = "mandato", Source = "system" }],
        };
        var repo = Substitute.For<IProcedureInstanceRepository>();
        repo.GetByIdWithFurGraphAsync(instance.Id, tenant, Arg.Any<CancellationToken>()).Returns(instance);
        var directory = Substitute.For<IMandateSignerDirectory>();
        directory.GetCandidatesAsync(office, tenant, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([Signer()]);

        var decision = await new MandatoApprovalHandler(repo, directory)
            .CheckAsync(instance.Id, tenant, null, null, TestContext.Current.CancellationToken);

        decision.Outcome.Should().Be(MandatoApprovalOutcome.Resolved);
        decision.Candidatos.Should().BeNull();
    }
}
