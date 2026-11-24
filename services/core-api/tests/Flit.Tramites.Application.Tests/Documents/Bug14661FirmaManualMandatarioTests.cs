using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances.ManualCapture;
using Flit.Tramites.Domain.Integration;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.Documents;

/// <summary>
/// Bug #14661 — la firma de la validación MANUAL del mandatario es un PNG de fondo blanco opaco. El mandato la estampaba
/// tal cual y el fondo tapaba las líneas del contrato. Ahora se prepara (fondo transparente) con el mismo extractor que la
/// firma manual de las partes; la de Kyverum no cambia.
/// </summary>
/// <example>
/// <code>
/// var r = await MandatarioFirmaResolver.ResolveAsync(vault, storage, tenantId,
///     signer with { RubricaIdentidadManual = true }, extractor, cancellationToken: ct);
/// // r.FirmaIdentidad == extractor.PrepareManualSignature(bytesGuardados)
/// </code>
/// </example>
public sealed class Bug14661FirmaManualMandatarioTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private const string RubricaPath = "identity/mandatario/firma-manual.png";
    private static readonly byte[] PngOpaco = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] PngListo = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 9, 9];

    private readonly IIdentitySignatureExtractor _extractor = Substitute.For<IIdentitySignatureExtractor>();

    private static MandateSignerCandidate Candidato(bool manual) =>
        new(
            Guid.NewGuid(), "Ana Restrepo", "1020304050", UserId: null, IdentityVigente: true,
            SignatureVaultId: null, TipoDocumento: "CC", CertificadoIdentidad: "SERIE-1",
            RubricaIdentidadPath: RubricaPath, RubricaIdentidadManual: manual);

    private static IAttachmentStorage StorageCon(byte[] contenido)
    {
        var storage = Substitute.For<IAttachmentStorage>();
        storage.OpenReadAsync(RubricaPath, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream?>(new MemoryStream(contenido)));
        return storage;
    }

    [Fact]
    public async Task FirmaManualDelMandatario_SeEstampaPreparadaConFondoTransparente()
    {
        var ct = TestContext.Current.CancellationToken;
        _extractor.PrepareManualSignature(Arg.Is<byte[]>(b => b.SequenceEqual(PngOpaco))).Returns(PngListo);

        var resultado = await MandatarioFirmaResolver.ResolveAsync(
            NullSignatureVaultPolicy.Instance, StorageCon(PngOpaco), TenantId, Candidato(manual: true), _extractor,
            cancellationToken: ct);

        resultado.FirmaIdentidad.Should().Equal(PngListo);
        resultado.Sello.Should().Be("Validación de identidad\nFirma SERIE-1");
    }

    [Fact]
    public async Task FirmaManualSinTinta_NoSeEstampaYQuedaElSello()
    {
        var ct = TestContext.Current.CancellationToken;
        _extractor.PrepareManualSignature(Arg.Any<byte[]>()).Returns((byte[]?)null);

        var resultado = await MandatarioFirmaResolver.ResolveAsync(
            NullSignatureVaultPolicy.Instance, StorageCon(PngOpaco), TenantId, Candidato(manual: true), _extractor,
            cancellationToken: ct);

        resultado.FirmaIdentidad.Should().BeNull();
        resultado.Sello.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task FirmaManualPorEncimaDelTopeDeLaCapturaManual_NoSeEstampaNiSeDecodifica()
    {
        var ct = TestContext.Current.CancellationToken;
        var grande = new byte[ManualCaptureImages.MaxSignatureBytes + 1];

        var resultado = await MandatarioFirmaResolver.ResolveAsync(
            NullSignatureVaultPolicy.Instance, StorageCon(grande), TenantId, Candidato(manual: true), _extractor,
            cancellationToken: ct);

        resultado.FirmaIdentidad.Should().BeNull();
        _extractor.DidNotReceive().PrepareManualSignature(Arg.Any<byte[]>());
    }

    [Fact]
    public async Task RubricaKyverum_SigueIgual_NoPasaPorElPreparadoManual()
    {
        var ct = TestContext.Current.CancellationToken;

        var resultado = await MandatarioFirmaResolver.ResolveAsync(
            NullSignatureVaultPolicy.Instance, StorageCon(PngOpaco), TenantId, Candidato(manual: false), _extractor,
            cancellationToken: ct);

        resultado.FirmaIdentidad.Should().Equal(PngOpaco);
        _extractor.DidNotReceive().PrepareManualSignature(Arg.Any<byte[]>());
    }

    [Fact]
    public async Task SinExtractor_ConservaElComportamientoPrevio()
    {
        var ct = TestContext.Current.CancellationToken;

        var resultado = await MandatarioFirmaResolver.ResolveAsync(
            NullSignatureVaultPolicy.Instance, StorageCon(PngOpaco), TenantId, Candidato(manual: true),
            cancellationToken: ct);

        resultado.FirmaIdentidad.Should().Equal(PngOpaco);
    }
}
