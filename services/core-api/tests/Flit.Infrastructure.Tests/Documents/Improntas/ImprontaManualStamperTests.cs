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

    private static byte[] MinimalPdf()
    {
        using var doc = new PdfSharpCore.Pdf.PdfDocument();
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
    public void Stamp_Manual_AddsMarkerAndDocumentHashLabel()
    {
        var pdf = MinimalPdf();
        var result = _sut.Stamp(pdf, Ctx(new ImprontaManualSigner("DANIEL FERNANDO GARCIA", "abc123", "hashprop", null)));

        result.Length.Should().BeGreaterThan(pdf.Length);
        _sut.AlreadyStamped(result).Should().BeTrue();
        Encoding.ASCII.GetString(result).Should().Contain(IImprontaManualStamper.MetadataKeyword);
        using var doc = PdfSharpCore.Pdf.IO.PdfReader.Open(new MemoryStream(result), PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Import);
        doc.PageCount.Should().Be(2);
    }

    [Fact]
    public void Stamp_AlreadyStamped_IsIdempotent()
    {
        var pdf = MinimalPdf();
        var once = _sut.Stamp(pdf, Ctx(new ImprontaManualSigner("A", null, null, null)));
        var twice = _sut.Stamp(once, Ctx(new ImprontaManualSigner("B", null, null, null)));

        twice.Should().Equal(once);
    }

    [Fact]
    public void Stamp_MultipleSigners_DoesNotThrow()
    {
        var pdf = MinimalPdf();
        var ctx = Ctx(
            new ImprontaManualSigner("UNO", "h1", "p1", null),
            new ImprontaManualSigner("DOS", "h2", "p2", null),
            new ImprontaManualSigner("TRES", "h3", "p3", null));

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
        doc.PageCount.Should().Be(2);
        Encoding.ASCII.GetString(stamped).Should().Contain($"DocHash:{expected}");
    }
}
