using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #13180 (Feature #13119 F7, Épica #13090) — el nivel «asociado de otra compañía» en el evaluador único
/// (gate de radicación, firmante previsto): refleja el nivel, valida vigencia y firma igual que los demás, resuelve
/// la firma del baúl en el tenant de la compañía propietaria y nunca expone documento ni ruta de firma.
/// <para>Uso de ejemplo: el mandatario de A asociado a B, con baúl vigente en el tenant de A, firma el trámite de B
/// y el firmante previsto responde <c>nivel = asociado_de_otra_compania</c>.</para>
/// </summary>
public sealed class MandatarioAsociadoPrelacionTests
{
    private static readonly Guid Office = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid TenantA = Guid.NewGuid();
    private const string DocumentoPii = "9988776655";

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IMandateSignerDirectory _directory = Substitute.For<IMandateSignerDirectory>();
    private readonly IMandateRequirementPolicy _policy = Substitute.For<IMandateRequirementPolicy>();
    private readonly IPersonalizedDocumentResolver _personalized = Substitute.For<IPersonalizedDocumentResolver>();
    private readonly ISignatureVaultPolicy _vault = Substitute.For<ISignatureVaultPolicy>();

    public MandatarioAsociadoPrelacionTests()
    {
        _personalized.ResolveAsync(Arg.Any<Guid>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(PersonalizedDocumentResolution.Empty);
        Config("signer");
    }

    private void Config(string mode, Guid? otDefault = null) =>
        _policy.ResolveByOfficeIdAsync(Office, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new MandateOtConfig(
                Office, "generico", false, null, null, AssignmentMode: mode, OtDefaultMandateSignerId: otDefault));

    private GetMandateSignerPrevistoHandler Sut() =>
        new(_repo, new MandateSignerEvaluator(_directory, _policy, _vault, _personalized),
            new TramiteValidationPolicy(
                TramiteValidationMode.Block, TramiteValidationMode.Block, TramiteValidationMode.Block,
                TramiteValidationMode.Block));

    private void VaultOnlyFor(Guid tenant)
    {
        _vault.ResolveMandatarioAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((SignatureVaultMatch?)null);
        _vault.ResolveMandatarioAsync(tenant, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SignatureVaultMatch(
                Guid.NewGuid(), "F", "h", "p", "s", DateOnly.MinValue, DateOnly.MaxValue, DocumentoPii));
    }

    private void Candidates(params MandateSignerCandidate[] c) =>
        _directory.GetCandidatesAsync(Office, TenantB, Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(c);

    private static MandateSignerCandidate Asociado(
        string nombre = "Ana de A", string metodo = MandateSignerOrigins.FormaBaul, bool firmaValida = true,
        string modelo = MandateSignerOrigins.ModeloNatural) =>
        new(Guid.NewGuid(), nombre, DocumentoPii, null, true, SignatureVaultId: Guid.NewGuid(),
            FirmaValida: firmaValida, MotivoSinFirma: firmaValida ? null : MandateSignerDiscardReasons.FueraDeVigencia,
            Origen: MandateSignerOrigins.Asociado, SignerModel: modelo, SignatureMethod: metodo,
            VaultTenantIds: [TenantA]);

    private static MandateSignerCandidate Propio(string nombre = "Beto de B", bool firmaValida = true) =>
        new(Guid.NewGuid(), nombre, "555", null, true,
            FirmaValida: firmaValida, MotivoSinFirma: firmaValida ? null : MandateSignerDiscardReasons.FueraDeVigencia,
            Origen: MandateSignerOrigins.Compania, SignerModel: MandateSignerOrigins.ModeloNatural,
            SignatureMethod: MandateSignerOrigins.FormaBiometria);

    private ProcedureInstance Seed()
    {
        var i = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = Guid.NewGuid(),
            TenantId = TenantB,
            Status = TramiteEstado.Borrador,
        };
        i.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "transit_office_id", ValueText = Office.ToString() });
        _repo.GetByIdWithDetailsAsync(i.Id, TenantB, Arg.Any<CancellationToken>()).Returns(i);
        return i;
    }

    private async Task<MandateSignerPrevistoDto> Consultar(ProcedureInstance i)
    {
        var (dto, error) = await Sut().HandleAsync(i.Id, TenantB, TestContext.Current.CancellationToken);
        error.Should().BeNull();
        return dto!;
    }

    // AC1 + AC6 — el asociado firma, el gate y el firmante previsto reflejan el nivel.
    [Fact]
    public async Task Ac6_ElAsociadoConBaulVigenteEnElTenantDeSuCompania_EsValido_ConNivelYNombre()
    {
        var i = Seed();
        VaultOnlyFor(TenantA); // la firma vive en el baúl de A; en el de B no hay nada
        Candidates(Asociado());

        var dto = await Consultar(i);

        dto.Estado.Should().Be("valido");
        dto.Nivel.Should().Be("asociado_de_otra_compania");
        dto.Nombre.Should().Be("Ana de A");
        dto.FormaFirma.Should().Be("baul");
        typeof(MandateSignerPrevistoDto).GetProperties().Select(p => p.Name).Should().NotContain(
            n => n.Contains("Document") || n.Contains("Path") || n.Contains("Signature"));
    }

    [Fact]
    public async Task Ac6_ElAsociadoSinFirmaEnElBaulDeSuCompaniaNiEnElDelTramite_EsBaulSinFirmaVigente()
    {
        var i = Seed();
        VaultOnlyFor(Guid.NewGuid()); // la firma está en un tercer tenant sin relación con A ni con B
        Candidates(Asociado());

        var dto = await Consultar(i);

        dto.Estado.Should().Be("firma_invalida");
        dto.Motivo.Should().Be(MandateSignerDiscardReasons.BaulSinFirmaVigente);
        dto.Nivel.Should().BeNull();
    }

    [Fact]
    public async Task Ac6_ElAsociadoConFirmaVencida_SeValidaComoLosDemasNiveles()
    {
        var i = Seed();
        Candidates(Asociado(metodo: MandateSignerOrigins.FormaBiometria, firmaValida: false));

        var dto = await Consultar(i);

        dto.Estado.Should().Be("sin_mandatario");
        dto.Motivo.Should().Be(MandateSignerDiscardReasons.FueraDeVigencia);
    }

    // AC2 — el propio de B prevalece.
    [Fact]
    public async Task Ac2_ElPropioDeLaCompaniaPrevaleceSobreElAsociado()
    {
        var i = Seed();
        VaultOnlyFor(TenantA);
        Candidates(Asociado(), Propio());

        var dto = await Consultar(i);

        dto.Nombre.Should().Be("Beto de B");
        dto.Nivel.Should().Be("propio_de_compania");
    }

    // AC4 — un propio vencido cuenta como inexistente y deja pasar al asociado.
    [Fact]
    public async Task Ac4_UnPropioVencido_CuentaComoInexistente_YFirmaElAsociado()
    {
        var i = Seed();
        VaultOnlyFor(TenantA);
        Candidates(Asociado(), Propio(firmaValida: false));

        var dto = await Consultar(i);

        dto.Nombre.Should().Be("Ana de A");
        dto.Nivel.Should().Be("asociado_de_otra_compania");
    }

    // AC5 — varios asociados: no se elige; el OT decide al aprobar.
    [Fact]
    public async Task Ac5_DosAsociadosDeCompaniasDistintas_NoSeEligeUno_PendienteEleccionDelOt()
    {
        var i = Seed();
        VaultOnlyFor(TenantA);
        Candidates(Asociado("Ana de A"), Asociado("Carlos de C"));

        var dto = await Consultar(i);

        dto.Estado.Should().Be("pendiente_eleccion_ot");
        dto.Nombre.Should().BeNull();
        dto.Nivel.Should().BeNull();
    }

    // AC7 — Persona jurídica / Mandato abierto: no hay firmante persona y no es bloqueo.
    [Theory]
    [InlineData("institutional")]
    [InlineData("open")]
    public async Task Ac7_ModosSinFirmantePersona_NoConsideranElNivelTresNiSonBloqueo(string modo)
    {
        Config(modo);
        var i = Seed();
        Candidates(Asociado());

        var dto = await Consultar(i);

        dto.Estado.Should().Be("no_aplica");
        dto.Nivel.Should().BeNull();
        await _directory.DidNotReceive().GetCandidatesAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Niveles_TienenCodigoEstableParaElIndicadorFirmara()
    {
        MandateSignerLevelCodes.ToCode(MandateSignerLevel.AsociadoDeOtraCompania).Should().Be("asociado_de_otra_compania");
        MandateSignerLevelCodes.ToCode(MandateSignerLevel.OtParaCompania).Should().Be("ot_para_compania");
        MandateSignerLevelCodes.ToCode(MandateSignerLevel.PropioDeCompania).Should().Be("propio_de_compania");
        MandateSignerLevelCodes.ToCode(MandateSignerLevel.DefaultDelOt).Should().Be("default_del_ot");
        MandateSignerLevelCodes.ToCode(MandateSignerLevel.Explicita).Should().Be("explicita");
        MandateSignerLevelCodes.ToCode(MandateSignerLevel.Ninguno).Should().Be("ninguno");
        new MandateSignerCandidate(Guid.NewGuid(), "x", "1", null, VaultTenantIds: [TenantA])
            .VaultTenants(TenantB).Should().Equal(TenantB, TenantA);
        new MandateSignerCandidate(Guid.NewGuid(), "x", "1", null).VaultTenants(TenantB).Should().Equal(TenantB);
    }
}
