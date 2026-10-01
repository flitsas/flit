using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
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
/// HU #13180b (Feature #13119 F7) — la firma del baúl de un mandatario se busca en el tenant correcto: el del trámite
/// y después los de sus compañías vinculadas. Sin esto, el default del OT (o un asociado) cuya firma vive en otra
/// compañía daba <c>baul_sin_firma_vigente</c> y, en modo <c>block</c>, bloqueaba radicaciones válidas.
/// <para>Uso de ejemplo: el default del OT vinculado a la compañía A, con baúl vigente en A, es válido para el trámite
/// de B y su firma se estampa; sin firma en ningún tenant sigue siendo <c>baul_sin_firma_vigente</c>.</para>
/// </summary>
public sealed class MandatarioBaulTenantCorrectoTests
{
    private static readonly Guid Office = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantOtra = Guid.NewGuid();
    private static readonly Guid DefaultOtId = Guid.NewGuid();
    private const string Documento = "9988776655";

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IMandateSignerDirectory _directory = Substitute.For<IMandateSignerDirectory>();
    private readonly IMandateRequirementPolicy _policy = Substitute.For<IMandateRequirementPolicy>();
    private readonly IPersonalizedDocumentResolver _personalized = Substitute.For<IPersonalizedDocumentResolver>();
    private readonly ISignatureVaultPolicy _vault = Substitute.For<ISignatureVaultPolicy>();

    public MandatarioBaulTenantCorrectoTests()
    {
        _personalized.ResolveAsync(Arg.Any<Guid>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(PersonalizedDocumentResolution.Empty);
        _policy.ResolveByOfficeIdAsync(Office, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new MandateOtConfig(
                Office, "generico", false, null, null, AssignmentMode: "signer", OtDefaultMandateSignerId: DefaultOtId));
        _directory.GetCandidatesAsync(Office, TenantB, Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    private static SignatureVaultMatch Firma() =>
        new(Guid.NewGuid(), "Ana", "h", "vault/firma.png", "s", DateOnly.MinValue, DateOnly.MaxValue, Documento);

    private void VaultSoloEn(params Guid[] tenants)
    {
        _vault.ResolveMandatarioAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((SignatureVaultMatch?)null);
        foreach (var t in tenants)
        {
            _vault.ResolveMandatarioAsync(t, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Firma());
        }
    }

    private static MandateSignerCandidate DefaultDelOt(IReadOnlyList<Guid>? vinculadas) =>
        new(DefaultOtId, "Default del OT", Documento, null, true, SignatureVaultId: null, TipoDocumento: "CC",
            Origen: MandateSignerOrigins.Organismo, SignerModel: MandateSignerOrigins.ModeloNatural,
            SignatureMethod: MandateSignerOrigins.FormaBaul, VaultTenantIds: vinculadas);

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

    private async Task<MandateSignerPrevistoDto> ConsultarAsync(ProcedureInstance i)
    {
        var sut = new GetMandateSignerPrevistoHandler(
            _repo, new MandateSignerEvaluator(_directory, _policy, _vault, _personalized),
            new TramiteValidationPolicy(
                TramiteValidationMode.Block, TramiteValidationMode.Block, TramiteValidationMode.Block,
                TramiteValidationMode.Block));
        var (dto, error) = await sut.HandleAsync(i.Id, TenantB, TestContext.Current.CancellationToken);
        error.Should().BeNull();
        return dto!;
    }

    [Fact]
    public async Task ElDefaultDelOtConFirmaEnSuCompania_EsValidoParaElTramiteDeOtraCompania()
    {
        var i = Seed();
        _directory.GetByIdAsync(DefaultOtId, Arg.Any<CancellationToken>()).Returns(DefaultDelOt([TenantA]));
        VaultSoloEn(TenantA);

        var dto = await ConsultarAsync(i);

        dto.Estado.Should().Be("valido");
        dto.Nivel.Should().Be("default_del_ot");
        dto.Nombre.Should().Be("Default del OT");
    }

    [Fact]
    public async Task ElDefaultDelOtSinFirmaEnNingunTenant_SigueSiendoBaulSinFirmaVigente()
    {
        var i = Seed();
        _directory.GetByIdAsync(DefaultOtId, Arg.Any<CancellationToken>()).Returns(DefaultDelOt([TenantA, TenantOtra]));
        VaultSoloEn(); // en ningún tenant

        var dto = await ConsultarAsync(i);

        dto.Estado.Should().Be("firma_invalida");
        dto.Motivo.Should().Be(MandateSignerDiscardReasons.BaulSinFirmaVigente);
    }

    [Fact]
    public async Task ElDefaultDelOtSinCompaniasVinculadas_SoloSeBuscaEnElTenantDelTramite()
    {
        var i = Seed();
        _directory.GetByIdAsync(DefaultOtId, Arg.Any<CancellationToken>()).Returns(DefaultDelOt(null));
        VaultSoloEn(TenantA); // la firma está en A, pero el mandatario no está vinculado a A

        (await ConsultarAsync(i)).Estado.Should().Be("firma_invalida");
    }

    [Fact]
    public async Task ElTenantDelTramiteSeConsultaPrimero_ComportamientoHistorico()
    {
        var c = DefaultDelOt([TenantA, TenantOtra]);
        VaultSoloEn(TenantB, TenantA);

        var match = await MandatarioBaulLookup.ResolveAsync(_vault, c, TenantB, TestContext.Current.CancellationToken);

        match.Should().NotBeNull();
        await _vault.Received(1).ResolveMandatarioAsync(
            TenantB, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _vault.DidNotReceive().ResolveMandatarioAsync(
            TenantA, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ElEstampadoUsaLaFirmaDelBaulDeLaCompaniaDelMandatario()
    {
        VaultSoloEn(TenantA);
        var storage = Substitute.For<IAttachmentStorage>();
        storage.OpenReadAsync("vault/firma.png", Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream?>(new MemoryStream([1, 2, 3])));

        var r = await MandatarioFirmaResolver.ResolveAsync(
            _vault, storage, TenantB, DefaultDelOt([TenantA]), null, TestContext.Current.CancellationToken);

        r.Firma.Should().NotBeNull("la firma del default del OT vive en el baúl de su compañía y debe estamparse");
        r.Metadatos.Should().NotBeNull();
    }

    [Fact]
    public async Task ElEstampado_SinFirmaEnNingunTenant_NoEstampaImagen()
    {
        VaultSoloEn();
        var storage = Substitute.For<IAttachmentStorage>();

        var r = await MandatarioFirmaResolver.ResolveAsync(
            _vault, storage, TenantB, DefaultDelOt([TenantA]), null, TestContext.Current.CancellationToken);

        r.Firma.Should().BeNull();
    }
}
