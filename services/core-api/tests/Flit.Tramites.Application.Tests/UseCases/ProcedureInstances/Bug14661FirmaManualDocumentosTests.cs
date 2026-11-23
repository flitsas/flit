using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
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
/// Bug #14661 — la firma capturada en el enlace manual es un PNG opaco de fondo blanco: no pasa el filtro de
/// transparencia de Kyverum (IsUsableInk) y el FUR caía en silencio al sello de texto sin la imagen.
/// </summary>
public sealed class Bug14661FirmaManualDocumentosTests
{
    private static readonly byte[] PngOpaco = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 9, 9, 9];
    private static readonly byte[] PngListo = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 7, 7, 7, 7];

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IKyverumCertificateClient _certClient = Substitute.For<IKyverumCertificateClient>();
    private readonly IRuesCertificateGenerator _rues = Substitute.For<IRuesCertificateGenerator>();
    private readonly IRnmcCertificateGenerator _rnmc = Substitute.For<IRnmcCertificateGenerator>();
    private readonly IProcedureInstancePrendaRepository _prenda = Substitute.For<IProcedureInstancePrendaRepository>();
    private readonly IAttachmentStorage _storage = Substitute.For<IAttachmentStorage>();
    private readonly IIdentitySignatureExtractor _extractor = Substitute.For<IIdentitySignatureExtractor>();
    private readonly Dictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);

    public Bug14661FirmaManualDocumentosTests()
    {
        _storage.OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<Stream?>(
                _blobs.TryGetValue(ci.ArgAt<string>(0), out var b) ? new MemoryStream(b) : null));
        _storage.SaveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(ci => new StoredFile($"{ci.ArgAt<Guid>(0):D}/{ci.ArgAt<string>(1)}", "sha", 1));
        _extractor.PrepareManualSignature(Arg.Is<byte[]>(b => b.SequenceEqual(PngOpaco))).Returns(PngListo);
        // Kyverum: el filtro de transparencia sigue siendo el de siempre; el PNG opaco NO pasa.
        _extractor.IsUsableInk(Arg.Any<byte[]>()).Returns(false);
    }

    private static ProcedureInstanceActor Actor(ProcedureInstance i, string rol, string doc, int ordinal) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = i.TenantId,
            ProcedureInstanceId = i.Id,
            ProcedureEntityId = Guid.NewGuid(),
            ActorType = rol,
            DocumentType = "CC",
            DocumentNumber = doc,
            FullName = $"{rol} {doc}",
            Ordinal = ordinal,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private ProcedureInstanceBiometricValidation Validacion(string rol, string doc, string provider, string path, byte[] firma)
    {
        _blobs[path] = firma;
        return new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            PartyRole = rol,
            Status = BiometricEstados.Aprobado,
            Provider = provider,
            KyverumVerificationId = provider == BiometricProviders.Kyverum ? $"kyv-{doc}" : null,
            ApprovalOrigin = provider == BiometricProviders.Manual ? BiometricApprovalOrigins.Manual : null,
            SignatureImagePath = path,
            Name = $"{rol} {doc}",
            DocumentType = "CC",
            DocumentNumber = doc,
            Email = "x@y.com",
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private async Task<FurDocumentData> Generar(string tipologia, params (string Rol, string Doc, int Ordinal, string Provider, byte[] Firma)[] partes)
    {
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For(tipologia),
            Id = id,
            TenantId = tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "FT1-0000522",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            FieldKey = "transit_office_code",
            ValueText = "11001000",
            Source = "user",
        });
        foreach (var p in partes)
        {
            instance.Actors.Add(Actor(instance, p.Rol, p.Doc, p.Ordinal));
            instance.BiometricValidations.Add(Validacion(p.Rol, p.Doc, p.Provider, $"bio/{p.Rol}-{p.Doc}.png", p.Firma));
        }

        _repo.GetByIdWithFurGraphAsync(id, tenant, Arg.Any<CancellationToken>()).Returns(instance);
        var capturing = new CapturingGenerator();
        var handler = new GenerarFurHandler(
            _repo, capturing, _certClient, _rues, _rnmc, _prenda, _storage,
            NullLogger<GenerarFurHandler>.Instance,
            identitySignatureExtractor: _extractor);

        var (_, error) = await handler.HandleAsync(id, tenant, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        capturing.Captured.Should().NotBeNull();
        return capturing.Captured!;
    }

    [Fact]
    public async Task Matricula_UnPropietarioManual_ElFurLlevaLaImagenDeLaFirma()
    {
        var data = await Generar(
            TramiteTipologiaCatalog.CodigoMatriculaInicial,
            ("comprador", "1", 1, BiometricProviders.Manual, PngOpaco));

        data.FirmaIdentidadImagenes.Should().NotBeNull();
        data.FirmaIdentidadImagenes!.Should().ContainKey("comprador");
        data.FirmaIdentidadImagenes!["comprador"].Should().Equal(PngListo);
    }

    [Fact]
    public async Task Matricula_DosPropietariosManuales_ElFurLlevaLaImagenDeCadaUno()
    {
        var data = await Generar(
            TramiteTipologiaCatalog.CodigoMatriculaInicial,
            ("comprador", "1", 1, BiometricProviders.Manual, PngOpaco),
            ("comprador", "2", 2, BiometricProviders.Manual, PngOpaco));

        data.FirmaIdentidadImagenes.Should().NotBeNull();
        data.FirmaIdentidadImagenes!.Keys.Should().BeEquivalentTo(["comprador", "comprador:2"]);
    }

    [Fact]
    public async Task Traspaso_DosVendedoresManuales_ElFurLlevaLaImagenDeCadaUno()
    {
        var data = await Generar(
            TramiteTipologiaCatalog.CodigoTraspasoStandard,
            ("comprador", "1", 1, BiometricProviders.Manual, PngOpaco),
            ("vendedor", "3", 1, BiometricProviders.Manual, PngOpaco),
            ("vendedor", "4", 2, BiometricProviders.Manual, PngOpaco));

        data.FirmaIdentidadImagenes.Should().NotBeNull();
        data.FirmaIdentidadImagenes!.Keys.Should().Contain(["comprador", "vendedor", "vendedor:2"]);
    }

    [Fact]
    public async Task Manual_PorEncimaDel400KbDeKyverum_PeroDentroDelTopeManual_SeEstampa()
    {
        var grande = new byte[600_000];
        PngOpaco.CopyTo(grande, 0);
        _extractor.PrepareManualSignature(Arg.Is<byte[]>(b => b.Length == grande.Length)).Returns(PngListo);

        var data = await Generar(
            TramiteTipologiaCatalog.CodigoMatriculaInicial,
            ("comprador", "1", 1, BiometricProviders.Manual, grande));

        data.FirmaIdentidadImagenes.Should().ContainKey("comprador");
    }

    [Fact]
    public async Task Manual_PorEncimaDelTopeManual_NoSeEstampa()
    {
        var enorme = new byte[(int)Flit.Tramites.Application.UseCases.ProcedureInstances.ManualCapture.ManualCaptureImages.MaxSignatureBytes + 1];
        PngOpaco.CopyTo(enorme, 0);

        var data = await Generar(
            TramiteTipologiaCatalog.CodigoMatriculaInicial,
            ("comprador", "1", 1, BiometricProviders.Manual, enorme));

        (data.FirmaIdentidadImagenes ?? new Dictionary<string, byte[]>()).Should().BeEmpty();
    }

    [Fact]
    public async Task Manual_SinTintaVisible_QuedaElSelloDeTexto()
    {
        var blanco = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 1, 1, 1 };
        _extractor.PrepareManualSignature(Arg.Is<byte[]>(b => b.SequenceEqual(blanco))).Returns((byte[]?)null);

        var data = await Generar(
            TramiteTipologiaCatalog.CodigoMatriculaInicial,
            ("comprador", "1", 1, BiometricProviders.Manual, blanco));

        (data.FirmaIdentidadImagenes ?? new Dictionary<string, byte[]>()).Should().BeEmpty();
        data.SellosIdentidad.Should().ContainKey("comprador");
    }

    [Fact]
    public async Task Kyverum_SigueFiltradoPorIsUsableInk_ElPngOpacoNoSeEstampa()
    {
        var data = await Generar(
            TramiteTipologiaCatalog.CodigoMatriculaInicial,
            ("comprador", "1", 1, BiometricProviders.Kyverum, PngOpaco));

        (data.FirmaIdentidadImagenes ?? new Dictionary<string, byte[]>()).Should().BeEmpty();
        _extractor.DidNotReceive().PrepareManualSignature(Arg.Any<byte[]>());
    }

    private sealed class CapturingGenerator : IFurDocumentGenerator
    {
        public FurDocumentData? Captured { get; private set; }

        public GeneratedDocument GenerateFur(FurDocumentData data)
        {
            Captured = data;
            return new GeneratedDocument("fur", "fur.pdf", "application/pdf", [1, 2, 3]);
        }

        public GeneratedDocument GenerateFurFillAll(FurTemplateFormat format = FurTemplateFormat.Automotor) =>
            new("fur", "fur_FILLALL.pdf", "application/pdf", [1, 2, 3]);

        public GeneratedDocument GenerateCompraventa(FurDocumentData data)
        {
            Captured = data;
            return new GeneratedDocument("compraventa", "cv.pdf", "application/pdf", [1, 2, 3]);
        }
    }
}
