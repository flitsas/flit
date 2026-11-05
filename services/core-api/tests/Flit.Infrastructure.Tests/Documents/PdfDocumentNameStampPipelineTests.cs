using System.Text;
using Flit.Infrastructure.Documents;
using Flit.Infrastructure.Documents.Branding;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharpCore.Pdf.IO;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

public sealed class PdfDocumentNameStampPipelineTests
{
    static PdfDocumentNameStampPipelineTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    [Fact]
    public void Apply_WhenStampSucceeds_DoesNotNormalize()
    {
        var pdf = MinimalPdf();
        var normalizeCalls = 0;
        var pipeline = new PdfDocumentNameStampPipeline(
            NullLogger.Instance,
            stamp: (bytes, name, _) => FlitPdfStamper.ApplyDocumentName(bytes, name),
            normalizeN1: _ => { normalizeCalls++; return null; },
            normalizeN2: _ => { normalizeCalls++; return null; });

        var result = pipeline.Apply(pdf, "Certificado RUES", FlitDocumentTheme.DocNameBottomCm);

        Encoding.UTF8.GetString(result.AsSpan(0, 4)).Should().Be("%PDF");
        normalizeCalls.Should().Be(0);
    }

    [Fact]
    public void Apply_WhenModifyFails_UsesN1AndStamps()
    {
        var original = MinimalPdf();
        var rewritten = MinimalPdf();
        var stampCalls = 0;
        var pipeline = new PdfDocumentNameStampPipeline(
            NullLogger.Instance,
            stamp: (bytes, name, bottom) =>
            {
                stampCalls++;
                if (ReferenceEquals(bytes, original))
                    throw new PdfReaderException("Token '18' was not expected.");
                return FlitPdfStamper.ApplyDocumentName(bytes, name, bottom);
            },
            normalizeN1: pdf =>
            {
                pdf.Should().BeSameAs(original);
                return rewritten;
            },
            normalizeN2: _ => throw new InvalidOperationException("N2 no debe invocarse si N1 alcanza."));

        var result = pipeline.Apply(original, "Homologación", FlitDocumentTheme.DocNameBottomCm);

        Encoding.UTF8.GetString(result.AsSpan(0, 4)).Should().Be("%PDF");
        stampCalls.Should().Be(2);
    }

    [Fact]
    public void Apply_WhenN1Insufficient_UsesN2AndStamps()
    {
        var original = MinimalPdf();
        var n1 = MinimalPdf();
        var n2 = MinimalPdf();
        var pipeline = new PdfDocumentNameStampPipeline(
            NullLogger.Instance,
            stamp: (bytes, name, bottom) =>
            {
                if (ReferenceEquals(bytes, n2))
                    return FlitPdfStamper.ApplyDocumentName(bytes, name, bottom);
                throw new PdfReaderException("Modify failed");
            },
            normalizeN1: _ => n1,
            normalizeN2: input =>
            {
                input.Should().BeSameAs(n1);
                return n2;
            });

        var result = pipeline.Apply(original, "Homologación", FlitDocumentTheme.DocNameBottomCm);

        Encoding.UTF8.GetString(result.AsSpan(0, 4)).Should().Be("%PDF");
        PageCount(result).Should().Be(PageCount(n2));
    }

    [Fact]
    public void Apply_WhenAllStrategiesFail_ReturnsNormalizedOrOriginalWithoutThrowing()
    {
        var original = MinimalPdf();
        var n1 = MinimalPdf();
        var pipeline = new PdfDocumentNameStampPipeline(
            NullLogger.Instance,
            stamp: (_, _, _) => throw new PdfReaderException("Modify failed"),
            normalizeN1: _ => n1,
            normalizeN2: _ => null);

        byte[]? result = null;
        var act = () => result = pipeline.Apply(original, "Homologación", FlitDocumentTheme.DocNameBottomCm);

        act.Should().NotThrow();
        result.Should().BeSameAs(n1);
    }

    [Fact]
    public void Compose_WhenPartStampFailsCompletely_StillProducesExpediente()
    {
        var good = MinimalPdf();
        var hostile = MinimalPdf();
        var pipeline = new PdfDocumentNameStampPipeline(
            NullLogger.Instance,
            stamp: (bytes, name, bottom) =>
            {
                if (name.Contains("HOSTIL", StringComparison.Ordinal))
                    throw new PdfReaderException("Token '18' was not expected.");
                return FlitPdfStamper.ApplyDocumentName(bytes, name, bottom);
            },
            normalizeN1: _ => null,
            normalizeN2: _ => null);
        var merger = new PdfExpedienteConsolidadoMerger(pipeline);

        var request = new MergeRequest(
            Parts:
            [
                new MergePart(good, "SOAT"),
                new MergePart(hostile, "HOSTIL Homologación"),
            ],
            Cover: null,
            EstadoTramite: "borrador");

        byte[]? composed = null;
        var act = () => composed = merger.Compose(request);

        act.Should().NotThrow();
        Encoding.UTF8.GetString(composed!.AsSpan(0, 4)).Should().Be("%PDF");
        PageCount(composed!).Should().Be(2);
    }

    [Fact]
    public void TryRewriteViaImport_OnQuestPdf_ProducesModifyOpenablePdf()
    {
        var pdf = MinimalPdf();

        var ok = PdfModifyNormalizer.TryRewriteViaImport(pdf, out var rewritten);

        ok.Should().BeTrue();
        using var ms = new MemoryStream(rewritten);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);
        doc.PageCount.Should().BeGreaterThan(0);
    }

    private static int PageCount(byte[] pdf)
    {
        using var ms = new MemoryStream(pdf);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.InformationOnly);
        return doc.PageCount;
    }

    private static byte[] MinimalPdf() =>
        Document.Create(c => c.Page(p => p.Content().Text("x"))).GeneratePdf();
}
