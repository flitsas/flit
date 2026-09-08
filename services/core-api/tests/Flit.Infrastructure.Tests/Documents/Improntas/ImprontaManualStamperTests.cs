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
    public void Stamp_Manual_AddsMarkerAndKeepsSamePageCount()
    {
        var pdf = MinimalPdf();
        var result = _sut.Stamp(pdf, Ctx(new ImprontaManualSigner("DANIEL FERNANDO GARCIA", "hashprop", null)));

        result.Length.Should().BeGreaterThan(pdf.Length);
        _sut.AlreadyStamped(result).Should().BeTrue();
        Encoding.ASCII.GetString(result).Should().Contain(IImprontaManualStamper.MetadataKeyword);
        using var doc = PdfSharpCore.Pdf.IO.PdfReader.Open(new MemoryStream(result), PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Import);
        doc.PageCount.Should().Be(1);
    }

    [Fact]
    public void Stamp_TwoPagePdf_DoesNotAddThirdPage()
    {
        var pdf = MinimalPdf(pages: 2);
        var result = _sut.Stamp(pdf, Ctx(new ImprontaManualSigner("ANA", null, null)));

        using var doc = PdfSharpCore.Pdf.IO.PdfReader.Open(new MemoryStream(result), PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Import);
        doc.PageCount.Should().Be(2);
    }

    [Fact]
    public void Stamp_AlreadyStamped_IsIdempotent()
    {
        var pdf = MinimalPdf();
        var once = _sut.Stamp(pdf, Ctx(new ImprontaManualSigner("A", null, null)));
        var twice = _sut.Stamp(once, Ctx(new ImprontaManualSigner("B", null, null)));

        twice.Should().Equal(once);
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
        _sut.AlreadyStamped(act()).Should().BeTrue();
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
        _sut.AlreadyStamped(stamped).Should().BeTrue();
        using var doc = PdfSharpCore.Pdf.IO.PdfReader.Open(new MemoryStream(stamped), PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Import);
        doc.PageCount.Should().Be(1);
        Encoding.ASCII.GetString(stamped).Should().Contain($"DocHash:{expected}");
    }

    [Fact]
    public void BuildFirmaDigital_IsRsa2048Base64LikeLegacy()
    {
        var hash = Convert.ToHexString(SHA256.HashData("doc"u8.ToArray())).ToLowerInvariant();
        var firma = ImprontaManualStamper.BuildFirmaDigital(hash);

        // RSA-2048 → 256 bytes → Base64 ~344 chars, padding "==".
        firma.Length.Should().BeInRange(340, 350);
        firma.Should().MatchRegex("^[A-Za-z0-9+/]+=*$");
        firma.Should().EndWith("=");
        // Sin hex concatenado (el MAC anterior mezclaba Base64 + hex).
        firma.Should().NotMatchRegex("[a-f0-9]{64}$");
    }

    [Fact]
    public void BuildFirmaDigital_IsDifferentEachCall_EphemeralKey()
    {
        var hash = Convert.ToHexString(SHA256.HashData("same"u8.ToArray())).ToLowerInvariant();
        var a = ImprontaManualStamper.BuildFirmaDigital(hash);
        var b = ImprontaManualStamper.BuildFirmaDigital(hash);
        a.Should().NotBe(b);
    }
}
