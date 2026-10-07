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

    // Bug #13304 — autocorrección: un artefacto persistido que el extractor ya no reconoce como
    // rúbrica (el logo «Verify» opaco) se re-extrae y se sobrescribe; uno bueno no se toca.

    [Fact]
    public async Task Bug13304_ArtefactoPersistidoSinAlfa_RecapturaYSobrescribe()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Kyverum("s3://logo");
        var logo = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 7, 7 };
        var rubrica = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 3, 3 };
        _store.OpenReadAsync("s3://logo", ct).Returns(new MemoryStream(logo));
        _extractor.IsUsableInk(Arg.Is<byte[]>(b => b.SequenceEqual(logo))).Returns(false);
        _extractor.IsUsableInk(Arg.Is<byte[]>(b => b.SequenceEqual(rubrica))).Returns(true);
        _extractor.TryExtract(Arg.Any<byte[]>()).Returns(new IdentitySignatureCrop(rubrica));
        _store.SaveAsync(v.TenantId, rubrica, ct).Returns(new StoredIdentitySignature("s3://rubrica", "f00d"));

        var outcome = await _sut.EnsureFromPdfAsync(v, [0x25, 0x50, 0x44, 0x46], ct);

        outcome.Should().Be(IdentitySignatureCaptureOutcome.Captured);
        v.SignatureImagePath.Should().Be("s3://rubrica");
        v.SignatureImageSha256.Should().Be("f00d");
        await _store.Received(1).SaveAsync(v.TenantId, rubrica, ct);
    }

    [Fact]
    public async Task Bug13304_ArtefactoBueno_NoRecaptura()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Kyverum("s3://bueno");
        var bueno = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 5, 5 };
        _store.OpenReadAsync("s3://bueno", ct).Returns(new MemoryStream(bueno));

        var outcome = await _sut.EnsureFromPdfAsync(v, [0x25, 0x50, 0x44, 0x46], ct);

        outcome.Should().Be(IdentitySignatureCaptureOutcome.AlreadyPresent);
        v.SignatureImagePath.Should().Be("s3://bueno");
        _extractor.DidNotReceiveWithAnyArgs().TryExtract(default!);
        await _store.DidNotReceive().SaveAsync(Arg.Any<Guid>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Bug13304_ArtefactoSinAlfa_CertificadoNoDisponible_EsRetryableYConservaElPath()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Kyverum("s3://logo");
        var logo = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 7, 7 };
        _store.OpenReadAsync("s3://logo", ct).Returns(new MemoryStream(logo));
        _extractor.IsUsableInk(Arg.Any<byte[]>()).Returns(false);
        _certs.DownloadCertificateAsync("kv-1", ct).Returns((KyverumCertificate?)null);

        var outcome = await _sut.EnsureAsync(v, ct);

        outcome.Should().Be(IdentitySignatureCaptureOutcome.Retryable);
        v.SignatureImagePath.Should().Be("s3://logo");
        await _certs.Received(1).DownloadCertificateAsync("kv-1", ct);
        _extractor.DidNotReceiveWithAnyArgs().TryExtract(default!);
        await _store.DidNotReceive().SaveAsync(Arg.Any<Guid>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Bug13304_ArtefactoSinAlfa_PdfSinRubricaReconocible_SeOmiteSinGuardar()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Kyverum("s3://logo");
        var logo = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 7, 7 };
        _store.OpenReadAsync("s3://logo", ct).Returns(new MemoryStream(logo));
        _extractor.IsUsableInk(Arg.Any<byte[]>()).Returns(false);
        _extractor.TryExtract(Arg.Any<byte[]>()).Returns((IdentitySignatureCrop?)null);

        var outcome = await _sut.EnsureFromPdfAsync(v, [0x25, 0x50, 0x44, 0x46], ct);

        outcome.Should().Be(IdentitySignatureCaptureOutcome.Skipped);
        v.SignatureImagePath.Should().Be("s3://logo");
        await _store.DidNotReceive().SaveAsync(Arg.Any<Guid>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
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
