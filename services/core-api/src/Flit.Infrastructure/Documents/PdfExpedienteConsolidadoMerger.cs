using Flit.Tramites.Application.Documents;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Fusiona adjuntos en un PDF único. PDFs se importan con PdfSharpCore; imágenes se
/// renderizan a una página con QuestPDF (ya presente por HU #10256).
/// </summary>
public sealed class PdfExpedienteConsolidadoMerger : IExpedienteConsolidadoMerger
{
    static PdfExpedienteConsolidadoMerger()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] NormalizeToPdf(byte[] content, string mimetype)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (string.Equals(mimetype, "application/pdf", StringComparison.OrdinalIgnoreCase))
            return content;

        if (IsImageMime(mimetype))
            return ImageToPdf(content);

        throw new NotSupportedException($"Mimetype no soportado para consolidado: {mimetype}");
    }

    public byte[] Merge(IReadOnlyList<byte[]> pdfParts)
    {
        ArgumentNullException.ThrowIfNull(pdfParts);
        if (pdfParts.Count == 0)
            throw new ArgumentException("Se requiere al menos un PDF para fusionar.", nameof(pdfParts));

        using var output = new PdfDocument();
        foreach (var pdfBytes in pdfParts)
        {
            using var inputStream = new MemoryStream(pdfBytes);
            using var input = PdfReader.Open(inputStream, PdfDocumentOpenMode.Import);
            for (var i = 0; i < input.PageCount; i++)
                output.AddPage(input.Pages[i]);
        }

        using var result = new MemoryStream();
        output.Save(result, false);
        return result.ToArray();
    }

    private static bool IsImageMime(string mimetype) =>
        string.Equals(mimetype, "image/jpeg", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mimetype, "image/png", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mimetype, "image/webp", StringComparison.OrdinalIgnoreCase);

    private static byte[] ImageToPdf(byte[] imageBytes) =>
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(24);
                page.Content().Image(imageBytes).FitArea();
            });
        }).GeneratePdf();
}
