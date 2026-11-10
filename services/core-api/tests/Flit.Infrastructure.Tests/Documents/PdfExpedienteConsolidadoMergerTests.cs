using System.Text;
using Flit.Infrastructure.Documents;
using Flit.Infrastructure.Documents.Branding;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

public sealed class PdfExpedienteConsolidadoMergerTests
{
    private static readonly PdfExpedienteConsolidadoMerger Merger = new();

    static PdfExpedienteConsolidadoMergerTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    [Fact]
    public void Merge_TwoSimplePdfs_ProducesPdfHeader()
    {
        var pdf1 = MinimalPdf();
        var pdf2 = MinimalPdf();

        var merged = Merger.Merge([pdf1, pdf2]);

        merged.Should().NotBeNullOrEmpty();
        Encoding.UTF8.GetString(merged.AsSpan(0, 4)).Should().Be("%PDF");
    }

    [Fact]
    public void NormalizeToPdf_PdfInput_ReturnsSameBytes()
    {
        var pdf = MinimalPdf();
        var normalized = Merger.NormalizeToPdf(pdf, "application/pdf");
        normalized.Should().BeEquivalentTo(pdf);
    }

    [Fact]
    public void NormalizeToPdf_PngInput_ProducesPdf()
    {
        var png = MinimalPng();
        var normalized = Merger.NormalizeToPdf(png, "image/png");

        Encoding.UTF8.GetString(normalized.AsSpan(0, 4)).Should().Be("%PDF");
    }

    // HU #14505 / ADR-0083 — magic bytes mandan sobre MIME (incidente FT1-0000372).
    [Fact]
    public void NormalizeToPdf_PdfBytesLabeledAsJpeg_TreatsAsPdf_WithoutQuestPdfImageDecode()
    {
        var pdf = MinimalPdf();
        var normalized = Merger.NormalizeToPdf(pdf, "image/jpeg");

        normalized.Should().BeEquivalentTo(pdf);
        Encoding.UTF8.GetString(normalized.AsSpan(0, 4)).Should().Be("%PDF");
    }

    [Fact]
    public void DetectContentKind_RecognizesPdfJpegPng()
    {
        PdfExpedienteConsolidadoMerger.DetectContentKind(MinimalPdf())
            .Should().Be(PdfExpedienteConsolidadoMerger.AttachmentContentKind.Pdf);
        PdfExpedienteConsolidadoMerger.DetectContentKind(MinimalPng())
            .Should().Be(PdfExpedienteConsolidadoMerger.AttachmentContentKind.Png);
        PdfExpedienteConsolidadoMerger.DetectContentKind(MinimalJpeg())
            .Should().Be(PdfExpedienteConsolidadoMerger.AttachmentContentKind.Jpeg);
        PdfExpedienteConsolidadoMerger.DetectContentKind([0x00, 0x01, 0x02])
            .Should().Be(PdfExpedienteConsolidadoMerger.AttachmentContentKind.Unknown);
    }

    [Fact]
    public void NormalizeToPdf_GarbageBytesLabeledAsJpeg_ReturnsPlaceholderPdf_WithoutThrowing()
    {
        var garbage = new byte[] { 0xFF, 0xD8, 0xFF, 0x00, 0x01, 0x02, 0x03 }; // JPEG SOI truncado/ilegible
        byte[]? normalized = null;
        var act = () => normalized = Merger.NormalizeToPdf(garbage, "image/jpeg");

        act.Should().NotThrow();
        Encoding.UTF8.GetString(normalized!.AsSpan(0, 4)).Should().Be("%PDF");
        PageCount(normalized!).Should().Be(1);
    }

    [Fact]
    public void NormalizeToPdf_JpegInput_ProducesPdf()
    {
        var jpeg = MinimalJpeg();
        var normalized = Merger.NormalizeToPdf(jpeg, "image/jpeg");

        Encoding.UTF8.GetString(normalized.AsSpan(0, 4)).Should().Be("%PDF");
    }

    [Fact]
    public void Compose_WithCover_PrependsExactlyOneCoverPage()
    {
        // HU #10857: la portada se antepone como primera página del expediente.
        var part = MinimalPdf();
        var request = new MergeRequest(
            Parts: [new MergePart(part, "FUR")],
            Cover: new ExpedienteCoverInfo("TRM-2026-000123", "ABC123", "Traspaso", "STT Medellín", "FLIT SAS"),
            EstadoTramite: "borrador");

        var composed = Merger.Compose(request);

        Encoding.UTF8.GetString(composed.AsSpan(0, 4)).Should().Be("%PDF");
        PageCount(composed).Should().Be(PageCount(part) + 1);
    }

    [Fact]
    public void Compose_WithoutCover_MergesPartsOnly()
    {
        var request = new MergeRequest(Parts: [new MergePart(MinimalPdf()), new MergePart(MinimalPdf())]);

        var composed = Merger.Compose(request);

        Encoding.UTF8.GetString(composed.AsSpan(0, 4)).Should().Be("%PDF");
        PageCount(composed).Should().Be(2);
    }

    [Theory]
    [InlineData("borrador")]   // no final → con marca de agua
    [InlineData("rechazado")]  // no final → con marca de agua
    [InlineData("aprobado")]   // final → sin marca de agua
    [InlineData("entregado")]  // final → sin marca de agua
    public void Compose_AppliesFooterAndWatermark_WithoutAlteringPageCount(string estado)
    {
        // HU #10858: pie por documento + marca de agua no cambian el número de páginas ni rompen el PDF.
        var request = new MergeRequest(
            Parts: [new MergePart(MinimalPdf(), "Certificado RUES"), new MergePart(MinimalPdf(), "SOAT")],
            Cover: null,
            EstadoTramite: estado);

        byte[]? composed = null;
        var act = () => composed = Merger.Compose(request);

        act.Should().NotThrow();
        Encoding.UTF8.GetString(composed!.AsSpan(0, 4)).Should().Be("%PDF");
        PageCount(composed!).Should().Be(2);
    }

    [Fact]
    public void BottomCmFor_ResolvesThemeMarginPerProfile()
    {
        // ADR-0049: prueba directa de la resolución perfil → margen que Compose invoca ANTES de
        // llamar a FlitPdfStamper.ApplyDocumentName para cada parte
        // (`FlitPdfStamper.ApplyDocumentName(part.Pdf, part.DocumentName!, BottomCmFor(part.Profile))`
        // — PdfExpedienteConsolidadoMerger.cs). No se compara el PDF compuesto byte a byte porque
        // PdfSharpCore asigna una etiqueta de subconjunto de fuente aleatoria en cada Save(): dos
        // invocaciones independientes de ApplyDocumentName con el MISMO texto no producen los mismos
        // bytes exactos aunque la geometría sí sea idéntica.
        PdfExpedienteConsolidadoMerger.BottomCmFor(StampProfile.Formulario)
            .Should().Be(FlitDocumentTheme.DocNameBottomFormularioCm);
        PdfExpedienteConsolidadoMerger.BottomCmFor(StampProfile.Default)
            .Should().Be(FlitDocumentTheme.DocNameBottomCm);
    }

    [Fact]
    public void Compose_MixedStampProfiles_DoesNotThrow_AndKeepsPageCount()
    {
        // Integración: MergeRequest con perfiles mixtos (Formulario para el FUR, Default para el
        // resto) llega completo a Compose sin lanzar, conservando una página por parte — la
        // geometría exacta por perfil está cubierta en BrandingModuleTests
        // (ApplyDocumentName_FormularioProfile_FallsWithinFurFreeBand /
        // ApplyDocumentName_DefaultProfile_OnLetterheadDocuments_StaysAboveFooterWhiteBand) y la
        // resolución perfil → margen en BottomCmFor_ResolvesThemeMarginPerProfile.
        var request = new MergeRequest(
            Parts:
            [
                new MergePart(MinimalPdf(), "Formulario Único de Registro (FUR)", StampProfile.Formulario),
                new MergePart(MinimalPdf(), "Certificado RUES", StampProfile.Default),
            ],
            Cover: null,
            EstadoTramite: null);

        byte[]? composed = null;
        var act = () => composed = Merger.Compose(request);

        act.Should().NotThrow();
        Encoding.UTF8.GetString(composed!.AsSpan(0, 4)).Should().Be("%PDF");
        PageCount(composed!).Should().Be(2);
    }

    [Fact]
    public void Compose_WithoutParts_Throws()
    {
        var request = new MergeRequest(Parts: []);

        var act = () => Merger.Compose(request);

        act.Should().Throw<ArgumentException>();
    }

    private static int PageCount(byte[] pdf)
    {
        using var ms = new MemoryStream(pdf);
        using var doc = PdfSharpCore.Pdf.IO.PdfReader.Open(ms, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.InformationOnly);
        return doc.PageCount;
    }

    private static byte[] MinimalPdf() =>
        Document.Create(c => c.Page(p => p.Content().Text("x"))).GeneratePdf();

    private static byte[] MinimalPng()
    {
        // 1×1 PNG transparente
        return Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
    }

    private static byte[] MinimalJpeg()
    {
        using var image = new Image<Rgba32>(1, 1);
        image[0, 0] = new Rgba32(200, 40, 40);
        using var ms = new MemoryStream();
        image.Save(ms, new JpegEncoder { Quality = 90 });
        return ms.ToArray();
    }
}
