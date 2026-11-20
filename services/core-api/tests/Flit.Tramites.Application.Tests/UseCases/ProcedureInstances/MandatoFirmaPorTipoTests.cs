using System.Text;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #14358 — el modo de firma del mandatario depende solo del tipo de mandato y de si hay estampa. Se
/// verifica por el camino real de generación (<see cref="GenerarFurHandler"/>), que es lo que llega al PDF.
/// <para>Uso de ejemplo: <c>var mandato = await GenerarAsync(Config("open"), ConEstampaDeIdentidad());</c>
/// y luego se inspecciona <c>mandato.ModoFirmaMandatario</c>.</para>
/// </summary>
public sealed class MandatoFirmaPorTipoTests
{
    private const string DocumentoCarlos = "222000222";
    private const string RutaPdf = "plantillas/mandato.pdf";
    private const string CuerpoPropio = "Plantilla propia {{placa}}";
    private const string Entidad = "UT-Demo";
    private const string NitEntidad = "900000000-1";

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid InstanceId = Guid.NewGuid();
    private static readonly Guid Ot = Guid.NewGuid();
    private static readonly Guid Carlos = Guid.Parse("cccccccc-2222-4000-8000-000000000002");
    private static readonly byte[] PdfPropio = Encoding.UTF8.GetBytes("%PDF PROPIO");

    // ── AC2 y AC3 — Persona natural ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC2_PersonaNatural_ConFirmaDelBaul_SaleEstampada()
    {
        var mandato = await GenerarAsync(Config(MandatoAssignmentModeCodes.Signer), ConEstampaDeIdentidad(), conBaul: true);

        mandato.Mandatario!.FirmaImagen.Should().NotBeNullOrEmpty();
        mandato.ModoFirmaMandatario.Should().Be(MandatarioFirmaModo.Estampada);
    }

    [Fact]
    public async Task AC2_PersonaNatural_ConSelloDeIdentidad_SaleEstampada()
    {
        var mandato = await GenerarAsync(Config(MandatoAssignmentModeCodes.Signer), ConEstampaDeIdentidad());

        mandato.Mandatario!.FirmaImagen.Should().BeNullOrEmpty();
        mandato.Mandatario.SelloIdentidad.Should().NotBeNullOrWhiteSpace();
        mandato.ModoFirmaMandatario.Should().Be(MandatarioFirmaModo.Estampada);
    }

    [Fact]
    public async Task AC3_PersonaNatural_SinEstampa_SaleConLineasManuales()
    {
        var mandato = await GenerarAsync(Config(MandatoAssignmentModeCodes.Signer), SinEstampa());

        mandato.Mandatario.Should().NotBeNull("el mandatario sigue nombrado, solo no tiene con qué estampar");
        mandato.ModoFirmaMandatario.Should().Be(MandatarioFirmaModo.Manual);
    }

    // ── AC4 — Persona jurídica ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AC4_PersonaJuridica_ConOSinEstampa_NoLlevaBloque_YCitaALaEntidad(bool conEstampa)
    {
        var config = Config(MandatoAssignmentModeCodes.Institutional) with
        {
            InstitutionalMandataryName = Entidad,
            InstitutionalMandataryNit = NitEntidad,
        };

        var mandato = await GenerarAsync(config, conEstampa ? ConEstampaDeIdentidad() : SinEstampa(), conBaul: conEstampa);

        mandato.ModoFirmaMandatario.Should().Be(MandatarioFirmaModo.SinBloque);
        mandato.InstitutionalMandataryName.Should().Be(Entidad, "los datos del mandatario siguen en el cuerpo");
        mandato.InstitutionalMandataryNit.Should().Be(NitEntidad);
    }

    // ── AC5 — Mandato abierto ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC5_MandatoAbierto_LineasManuales_AunqueExistaEstampa_YSinMandatarioAsignado()
    {
        var mandato = await GenerarAsync(Config(MandatoAssignmentModeCodes.Open), ConEstampaDeIdentidad(), conBaul: true);

        mandato.Mandatario.Should().BeNull();
        mandato.ModoFirmaMandatario.Should().Be(MandatarioFirmaModo.Manual);
    }

    // ── AC6 — tipo ausente o desconocido ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tipo-que-no-existe")]
    public async Task AC6_TipoAusenteODesconocido_SeComportaComoPersonaNatural(string? tipo)
    {
        var conEstampa = await GenerarAsync(Config(tipo), ConEstampaDeIdentidad());
        var sinEstampa = await GenerarAsync(Config(tipo), SinEstampa());

        conEstampa.ModoFirmaMandatario.Should().Be(MandatarioFirmaModo.Estampada);
        sinEstampa.ModoFirmaMandatario.Should().Be(MandatarioFirmaModo.Manual);
    }

    // ── AC7 — plantilla personalizada ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(MandatoCustomTemplateKindCodes.Editor, MandatoAssignmentModeCodes.Signer, true, MandatarioFirmaModo.Estampada)]
    [InlineData(MandatoCustomTemplateKindCodes.Editor, MandatoAssignmentModeCodes.Signer, false, MandatarioFirmaModo.Manual)]
    [InlineData(MandatoCustomTemplateKindCodes.Editor, MandatoAssignmentModeCodes.Open, true, MandatarioFirmaModo.Manual)]
    [InlineData(MandatoCustomTemplateKindCodes.Editor, MandatoAssignmentModeCodes.Institutional, true, MandatarioFirmaModo.SinBloque)]
    [InlineData(MandatoCustomTemplateKindCodes.Pdf, MandatoAssignmentModeCodes.Signer, true, MandatarioFirmaModo.Estampada)]
    [InlineData(MandatoCustomTemplateKindCodes.Pdf, MandatoAssignmentModeCodes.Signer, false, MandatarioFirmaModo.Manual)]
    [InlineData(MandatoCustomTemplateKindCodes.Pdf, MandatoAssignmentModeCodes.Open, true, MandatarioFirmaModo.Manual)]
    [InlineData(MandatoCustomTemplateKindCodes.Pdf, MandatoAssignmentModeCodes.Institutional, true, MandatarioFirmaModo.SinBloque)]
    public async Task AC7_PlantillaPersonalizada_SeUsaLaMisma_YElModoEsElDelTipo(
        string kind, string tipo, bool conEstampa, MandatarioFirmaModo esperado)
    {
        var config = Config(tipo) with
        {
            CustomTemplateKind = kind,
            CustomTemplateBody = kind == MandatoCustomTemplateKindCodes.Editor ? CuerpoPropio : null,
            CustomTemplateStoragePath = kind == MandatoCustomTemplateKindCodes.Pdf ? RutaPdf : null,
        };
        var blob = Substitute.For<IMandateCustomTemplateBlobReader>();
        blob.OpenPdfAsync(RutaPdf, Arg.Any<CancellationToken>()).Returns(PdfPropio);

        var mandato = await GenerarAsync(config, conEstampa ? ConEstampaDeIdentidad() : SinEstampa(), blob: blob);

        mandato.CustomTemplateKind.Should().Be(kind);
        if (kind == MandatoCustomTemplateKindCodes.Editor)
            mandato.CustomTemplateBody.Should().Be(CuerpoPropio);
        else
            mandato.CustomTemplatePdf.Should().Equal(PdfPropio);
        mandato.ModoFirmaMandatario.Should().Be(esperado);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    private static MandateOtConfig Config(string? tipo) => new(
        Ot,
        MandatoTemplateResolver.Generico,
        RequiresForNaturalPerson: false,
        InstitutionalMandataryName: null,
        InstitutionalMandataryNit: null,
        AssignmentMode: tipo,
        OtDefaultMandateSignerId: Carlos);

    /// <summary>Identidad vigente: sin firma del baúl, el contrato le pone el sello de esa validación.</summary>
    private static MandateSignerCandidate ConEstampaDeIdentidad() => new(
        Carlos, "Carlos Pérez Demo", DocumentoCarlos, null, IdentityVigente: true, TipoDocumento: "CC");

    private static MandateSignerCandidate SinEstampa() => new(
        Carlos, "Carlos Pérez Demo", DocumentoCarlos, null, IdentityVigente: false, TipoDocumento: "CC");

    private static async Task<MandatoData> GenerarAsync(
        MandateOtConfig config,
        MandateSignerCandidate candidato,
        bool conBaul = false,
        IMandateCustomTemplateBlobReader? blob = null)
    {
        var policy = Substitute.For<IMandateRequirementPolicy>();
        policy.ResolveByOfficeIdAsync(Ot, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(config);

        var repo = Substitute.For<IProcedureInstanceRepository>();
        repo.GetByIdWithFurGraphAsync(InstanceId, TenantId, Arg.Any<CancellationToken>()).Returns(NewInstance());

        var generador = new CapturingMandatoGenerator();
        var handler = new GenerarFurHandler(
            repo,
            new MockFurDocumentGenerator(),
            Substitute.For<IKyverumCertificateClient>(),
            Substitute.For<IRuesCertificateGenerator>(),
            Substitute.For<IRnmcCertificateGenerator>(),
            Substitute.For<IProcedureInstancePrendaRepository>(),
            new StorageConFirma(),
            NullLogger<GenerarFurHandler>.Instance,
            vaultPolicy: conBaul ? BaulConFirma() : null,
            mandatoGenerator: generador,
            mandatePolicy: policy,
            mandateDirectory: new Directorio(candidato),
            mandateTemplateBlobReader: blob);

        var (_, error) = await handler.HandleAsync(InstanceId, TenantId, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        generador.Captured.Should().NotBeNull();
        return generador.Captured!;
    }

    private static ISignatureVaultPolicy BaulConFirma()
    {
        var match = new SignatureVaultMatch(
            SignatureVaultId: Guid.NewGuid(),
            FullName: "Carlos Pérez Demo",
            SignatureHash: "sig-hash",
            StoragePath: "firmas/carlos.png",
            StorageSha256: "sha-firma",
            VigenciaDesde: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1)),
            VigenciaHasta: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
            DocumentNumber: DocumentoCarlos,
            CodigoHash: "ABC123");
        var vault = Substitute.For<ISignatureVaultPolicy>();
        vault.ResolveAsync(TenantId, "CC", DocumentoCarlos, Arg.Any<CancellationToken>()).Returns(match);
        vault.ResolveMandatarioAsync(TenantId, "CC", DocumentoCarlos, Arg.Any<CancellationToken>()).Returns(match);
        return vault;
    }

    private static ProcedureInstance NewInstance() => new()
    {
        ProcedureType = ProcedureTypeFixture.For(TramiteTipologiaCatalog.CodigoMatriculaInicial ?? "matricula_inicial"),
        Id = InstanceId,
        TenantId = TenantId,
        ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = "TRM-2026-000777",
        Status = TramiteEstado.Borrador,
        TransitOfficeId = Ot,
        MandateSignerId = null,
        CreatedAt = DateTimeOffset.UtcNow,
        FieldValues =
        {
            new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProcedureInstanceId = InstanceId,
                FieldKey = "transit_office_code",
                ValueText = "11001000",
                Source = "user",
            },
        },
    };

    private sealed class Directorio(MandateSignerCandidate candidato) : IMandateSignerDirectory
    {
        public Task<IReadOnlyList<MandateSignerCandidate>> GetCandidatesAsync(
            Guid transitOfficeId, Guid companyTenantId, string? nitMandante = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MandateSignerCandidate>>(
                transitOfficeId == Ot && companyTenantId == TenantId ? [candidato] : []);

        public Task<MandateSignerCandidate?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(id == candidato.Id ? candidato : null);
    }

    private sealed class CapturingMandatoGenerator : IMandatoGenerator
    {
        public MandatoData? Captured { get; private set; }

        public GeneratedDocument GenerateMandato(MandatoData data)
        {
            Captured = data;
            return new GeneratedDocument(
                "mandato", "mandato.pdf", "application/pdf", Encoding.UTF8.GetBytes("%PDF MANDATO"));
        }
    }

    private sealed class StorageConFirma : IAttachmentStorage
    {
        public Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content,
            CancellationToken ct = default) =>
            Task.FromResult(new StoredFile($"{procedureInstanceId:D}/{tipo}", $"sha-{tipo}", 10));

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath)
        {
        }

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes("PNG-FIRMA")));

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }
}
