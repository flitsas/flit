using Flit.Tramites.Application.Identity;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class IdentitySignatureCaptureTests
{
    private readonly IKyverumCertificateClient _certs = Substitute.For<IKyverumCertificateClient>();
    private readonly IIdentitySignatureExtractor _extractor = Substitute.For<IIdentitySignatureExtractor>();
    private readonly IIdentitySignatureArtifactStorage _store = Substitute.For<IIdentitySignatureArtifactStorage>();
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IdentitySignatureCapture _sut;

    public IdentitySignatureCaptureTests()
    {
        _extractor.IsUsableInk(Arg.Any<byte[]>()).Returns(true);
        _sut = new IdentitySignatureCapture(_certs, _extractor, _store, _repo, NullLogger<IdentitySignatureCapture>.Instance);
    }

    private static ProcedureInstanceBiometricValidation Kyverum(string? path = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        Provider = BiometricProviders.Kyverum,
        KyverumVerificationId = "kv-1",
        Status = BiometricEstados.Aprobado,
        SignatureImagePath = path,
        SignatureImageSha256 = path is null ? null : "abc",
    };

    [Fact]
    public async Task YaCapturada_NoVuelveAKyverum()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Kyverum("s3://x");

        var pngHeader = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 1 };
        _store.OpenReadAsync("s3://x", ct).Returns(new MemoryStream(pngHeader));

        var outcome = await _sut.EnsureAsync(v, ct);

        outcome.Should().Be(IdentitySignatureCaptureOutcome.AlreadyPresent);
        await _certs.DidNotReceiveWithAnyArgs().DownloadCertificateAsync(default!, ct);
    }

    [Fact]
    public async Task ArtefactoInvalido_RecapturaDesdePdf()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Kyverum("s3://basura");
        _store.OpenReadAsync("s3://basura", ct).Returns(new MemoryStream([0x78, 0x9C, 0x01, 0x02]));
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        _extractor.TryExtract(Arg.Any<byte[]>()).Returns(new IdentitySignatureCrop(png));
        _store.SaveAsync(v.TenantId, png, ct).Returns(new StoredIdentitySignature("path-2", "cafecafe"));

        var outcome = await _sut.EnsureFromPdfAsync(v, [0x25, 0x50, 0x44, 0x46], ct);

        outcome.Should().Be(IdentitySignatureCaptureOutcome.Captured);
        v.SignatureImagePath.Should().Be("path-2");
    }

    [Fact]
    public async Task ArtefactoSinTinta_RecapturaDesdePdf()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Kyverum("s3://negro");
        var slab = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
        var ink = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 9 };
        _store.OpenReadAsync("s3://negro", ct).Returns(new MemoryStream(slab));
        _extractor.IsUsableInk(Arg.Any<byte[]>()).Returns(false, true);
        _extractor.TryExtract(Arg.Any<byte[]>()).Returns(new IdentitySignatureCrop(ink));
        _store.SaveAsync(v.TenantId, ink, ct).Returns(new StoredIdentitySignature("path-ink", "ab"));

        var outcome = await _sut.EnsureFromPdfAsync(v, [0x25, 0x50, 0x44, 0x46], ct);

        outcome.Should().Be(IdentitySignatureCaptureOutcome.Captured);
        v.SignatureImagePath.Should().Be("path-ink");
    }

    [Fact]
    public async Task Mock_SeOmite()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Kyverum();
        v.Provider = BiometricProviders.Mock;
        v.KyverumVerificationId = null;

        (await _sut.EnsureAsync(v, ct)).Should().Be(IdentitySignatureCaptureOutcome.Skipped);
    }

    [Fact]
    public async Task PdfAusente_EsRetryable()
    {
        var ct = TestContext.Current.CancellationToken;
        _certs.DownloadCertificateAsync("kv-1", ct).Returns((KyverumCertificate?)null);

        (await _sut.EnsureAsync(Kyverum(), ct)).Should().Be(IdentitySignatureCaptureOutcome.Retryable);
    }

    [Fact]
    public async Task RecorteOk_PersistePathYHash()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Kyverum();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2 };
        _extractor.TryExtract(Arg.Any<byte[]>()).Returns(new IdentitySignatureCrop(png));
        _store.SaveAsync(v.TenantId, png, ct).Returns(new StoredIdentitySignature("path-1", "deadbeef"));

        var outcome = await _sut.EnsureFromPdfAsync(v, [0x25, 0x50, 0x44, 0x46], ct);

        outcome.Should().Be(IdentitySignatureCaptureOutcome.Captured);
        v.SignatureImagePath.Should().Be("path-1");
        v.SignatureImageSha256.Should().Be("deadbeef");
    }

    [Fact]
    public async Task SinImagenEnPdf_Skipped_NoTira()
    {
        var ct = TestContext.Current.CancellationToken;
        _extractor.TryExtract(Arg.Any<byte[]>()).Returns((IdentitySignatureCrop?)null);

        (await _sut.EnsureFromPdfAsync(Kyverum(), [0x25, 0x50], ct))
            .Should().Be(IdentitySignatureCaptureOutcome.Skipped);
    }
}
