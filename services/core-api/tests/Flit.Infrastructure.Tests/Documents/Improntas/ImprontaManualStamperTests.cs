using System.Security.Cryptography;
using System.Text;
using Flit.Infrastructure.Documents.Improntas;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents.Improntas;

public sealed class ImprontaManualStamperTests
{
    private readonly ImprontaManualStamper _sut = new();

    private static byte[] MinimalPdf(int pages = 1)
    {
        using var doc = new PdfSharpCore.Pdf.PdfDocument();
        for (var i = 0; i < pages; i++)
            doc.AddPage();
        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }

    private static void AssertImportableRsaPemPair(string privateKeyPem, string publicKeyPem)
    {
        privateKeyPem.Should().NotBeNullOrWhiteSpace();
        publicKeyPem.Should().NotBeNullOrWhiteSpace();
        using var privateRsa = RSA.Create();
        privateRsa.ImportFromPem(privateKeyPem);
        using var publicRsa = RSA.Create();
        publicRsa.ImportFromPem(publicKeyPem);
    }

    private static ImprontaManualStampContext Ctx(params ImprontaManualSigner[] signers) =>
        new(
            ReferenceNumber: "FLIT-0234659",
            Placa: "POV420",
            Vin: "9BGEA76C0SB252602",
            NumMotor: "L4H250865053",
            NumChasis: "9BGEA76C0SB252602",
            FechaCargue: new DateTimeOffset(2026, 9, 5, 11, 11, 33, TimeSpan.FromHours(-5)),
            Signers: signers);

    [Fact]
    public void FormatOwnerHashLabel_SingleSigner_IsUnindexed()
    {
        ImprontaManualStamper.FormatOwnerHashLabel(0, 1, "solo-hash")
            .Should().Be("Hash propietario: solo-hash");
    }

    [Fact]
    public void FormatOwnerHashLabel_MultipleSigners_UsesOneBasedIndex()
    {
        ImprontaManualStamper.FormatOwnerHashLabel(0, 2, "p1")
            .Should().Be("Hash propietario1: p1");
        ImprontaManualStamper.FormatOwnerHashLabel(1, 2, "p2")
            .Should().Be("Hash propietario2: p2");
        ImprontaManualStamper.FormatOwnerHashLabel(3, 4, "h4")
            .Should().Be("Hash propietario4: h4");
    }

    [Fact]
    public void UsesSharedMetadataBlock_IsTrueWhenMultipleSigners()
    {
        ImprontaManualStamper.UsesSharedMetadataBlock(1).Should().BeFalse();
        ImprontaManualStamper.UsesSharedMetadataBlock(2).Should().BeTrue();
        ImprontaManualStamper.UsesSharedMetadataBlock(4).Should().BeTrue();
    }

    [Fact]
    public void StacksOwnerHashesVertically_IsTrueWhenMultipleSigners()
    {
        ImprontaManualStamper.StacksOwnerHashesVertically(1).Should().BeFalse();
        ImprontaManualStamper.StacksOwnerHashesVertically(2).Should().BeTrue();
        ImprontaManualStamper.StacksOwnerHashesVertically(4).Should().BeTrue();
    }

    [Fact]
    public void EstimateLayoutHeight_MultiOwner_IsShorterThanLegacyPerColumnRepeat()
    {
        ImprontaManualStamper.EstimateLayoutHeightForTest(2)
            .Should().BeLessThan(ImprontaManualStamper.EstimateLegacyRepeatedHeightForTest(2));
        ImprontaManualStamper.EstimateLayoutHeightForTest(4)
            .Should().BeLessThan(ImprontaManualStamper.EstimateLegacyRepeatedHeightForTest(4));
    }

    [Fact]
    public void EstimateLayoutHeight_FourSigners_UsesCompactSignatureBand()
    {
        ImprontaManualStamper.EstimateLayoutHeightForTest(4)
            .Should().BeLessThan(ImprontaManualStamper.EstimateLayoutHeightForTest(2) + 40);
    }

    [Fact]
    public void Stamp_Manual_AddsMarkerAndKeepsSamePageCount()
    {
        var pdf = MinimalPdf();
        var result = _sut.Stamp(pdf, Ctx(new ImprontaManualSigner("DANIEL FERNANDO GARCIA", "hashprop", null)));

        result.Applied.Should().BeTrue();
        result.Pdf.Length.Should().BeGreaterThan(pdf.Length);
        AssertImportableRsaPemPair(result.PrivateKeyPem, result.PublicKeyPem);
        result.SignatureBase64.Length.Should().BeInRange(340, 350);
        _sut.AlreadyStamped(result.Pdf).Should().BeTrue();
        Encoding.ASCII.GetString(result.Pdf).Should().Contain(IImprontaManualStamper.MetadataKeyword);
        using var doc = PdfSharpCore.Pdf.IO.PdfReader.Open(new MemoryStream(result.Pdf), PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Import);
        doc.PageCount.Should().Be(1);
    }

    [Fact]
    public void Stamp_TwoPagePdf_DoesNotAddThirdPage()
    {
        var pdf = MinimalPdf(pages: 2);
        var result = _sut.Stamp(pdf, Ctx(new ImprontaManualSigner("ANA", null, null)));

        using var doc = PdfSharpCore.Pdf.IO.PdfReader.Open(new MemoryStream(result.Pdf), PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Import);
        doc.PageCount.Should().Be(2);
    }

    [Fact]
    public void Stamp_AlreadyStamped_IsIdempotent()
    {
        var pdf = MinimalPdf();
        var once = _sut.Stamp(pdf, Ctx(new ImprontaManualSigner("A", null, null)));
        var twice = _sut.Stamp(once.Pdf, Ctx(new ImprontaManualSigner("B", null, null)));

        twice.Applied.Should().BeFalse();
        twice.Pdf.Should().Equal(once.Pdf);
    }

    [Fact]
    public void Stamp_TwoSigners_AppliesStamp()
    {
        var pdf = MinimalPdf();
        var ctx = Ctx(
            new ImprontaManualSigner("UNO", "owner-hash-alpha", null),
            new ImprontaManualSigner("DOS", "owner-hash-beta", null));

        var result = _sut.Stamp(pdf, ctx);

        result.Applied.Should().BeTrue();
        _sut.AlreadyStamped(result.Pdf).Should().BeTrue();
        using var doc = PdfSharpCore.Pdf.IO.PdfReader.Open(new MemoryStream(result.Pdf), PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Import);
        doc.PageCount.Should().Be(1);
    }

    [Fact]
    public void Stamp_FourSigners_AppliesWithoutOverflow()
    {
        var pdf = MinimalPdf();
        var ctx = Ctx(
            new ImprontaManualSigner("UNO", "h1", null),
            new ImprontaManualSigner("DOS", "h2", null),
            new ImprontaManualSigner("TRES", "h3", null),
            new ImprontaManualSigner("CUATRO", "h4", null));

        var result = _sut.Stamp(pdf, ctx);

        result.Applied.Should().BeTrue();
        _sut.AlreadyStamped(result.Pdf).Should().BeTrue();
        using var doc = PdfSharpCore.Pdf.IO.PdfReader.Open(new MemoryStream(result.Pdf), PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Import);
        doc.PageCount.Should().Be(1);
        Encoding.ASCII.GetString(result.Pdf).Should().Contain(IImprontaManualStamper.MetadataKeyword);
    }

    [Fact]
    public void Stamp_MultipleSigners_DoesNotThrow()
    {
        var pdf = MinimalPdf();
        var ctx = Ctx(
            new ImprontaManualSigner("UNO", "p1", null),
            new ImprontaManualSigner("DOS", "p2", null),
            new ImprontaManualSigner("TRES", "p3", null));

        var act = () => _sut.Stamp(pdf, ctx);
        act.Should().NotThrow();
        act().Applied.Should().BeTrue();
        _sut.AlreadyStamped(act().Pdf).Should().BeTrue();
    }

    [Fact]
    public void AlreadyStamped_RawPdfWithoutMarker_IsFalse()
    {
        _sut.AlreadyStamped(MinimalPdf()).Should().BeFalse();
    }

    [Fact]
    public void DocumentHash_IsEmbeddedInAsciiTrailer()
    {
        var pdf = MinimalPdf();
        var expected = Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant();
        var stamped = _sut.Stamp(pdf, Ctx());
        stamped.DocumentHash.Should().Be(expected);
        _sut.AlreadyStamped(stamped.Pdf).Should().BeTrue();
        using var doc = PdfSharpCore.Pdf.IO.PdfReader.Open(new MemoryStream(stamped.Pdf), PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Import);
        doc.PageCount.Should().Be(1);
        Encoding.ASCII.GetString(stamped.Pdf).Should().Contain($"DocHash:{expected}");
    }

    [Fact]
    public void BuildFirmaDigital_IsRsa2048Base64LikeLegacy()
    {
        var hash = Convert.ToHexString(SHA256.HashData("doc"u8.ToArray())).ToLowerInvariant();
        var firma = ImprontaManualStamper.BuildFirmaDigital(hash);

        firma.SignatureBase64.Length.Should().BeInRange(340, 350);
        firma.SignatureBase64.Should().MatchRegex("^[A-Za-z0-9+/]+=*$");
        firma.SignatureBase64.Should().EndWith("=");
        AssertImportableRsaPemPair(firma.PrivateKeyPem, firma.PublicKeyPem);
    }

    [Fact]
    public void BuildFirmaDigital_IsDifferentEachCall_EphemeralKey()
    {
        var hash = Convert.ToHexString(SHA256.HashData("same"u8.ToArray())).ToLowerInvariant();
        var a = ImprontaManualStamper.BuildFirmaDigital(hash);
        var b = ImprontaManualStamper.BuildFirmaDigital(hash);
        a.SignatureBase64.Should().NotBe(b.SignatureBase64);
    }
}
