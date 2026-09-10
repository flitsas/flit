using Flit.Tramites.Application.Ocr;
using Microsoft.Extensions.Logging;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace Flit.Infrastructure.Ocr;

/// <summary>
/// Recorta un subconjunto de páginas de un PDF con PdfSharpCore (misma librería que el resto de la
/// generación PDF de Infrastructure). Límites de seguridad: no procesa PDFs de más de 200 páginas y
/// extrae como máximo 20. Ante PDF ilegible o error, devuelve null (degradación: se sube el original).
/// </summary>
internal sealed class PdfSharpPageExtractor(ILogger<PdfSharpPageExtractor> logger) : IPdfPageExtractor
{
    private const int MaxSourcePages = 200;
    private const int MaxExtractedPages = 20;

    public int? CountPages(ReadOnlyMemory<byte> pdf)
    {
        try
        {
            using var stream = new MemoryStream(pdf.ToArray(), writable: false);
            using var doc = PdfReader.Open(stream, PdfDocumentOpenMode.InformationOnly);
            return doc.PageCount;
        }
        catch (Exception ex) when (ex is PdfReaderException or InvalidOperationException or IOException or ArgumentException or NotSupportedException)
        {
            PdfExtractLog.ExtractFailed(logger, ex.Message);
            return null;
        }
    }

    public byte[]? Rotate(ReadOnlyMemory<byte> pdf, int quarterTurns)
    {
        // Girar es sumar al /Rotate que el PDF ya declara. Nada de rasterizar: el visor —y el modelo de
        // visión— aplican ese campo al renderizar, así que basta con reescribirlo. El PDF admite solo
        // múltiplos de 90 y los normaliza a [0,360).
        var delta = ((quarterTurns % 4) + 4) % 4 * 90;
        if (delta == 0)
            return pdf.ToArray();

        try
        {
            using var stream = new MemoryStream(pdf.ToArray(), writable: false);
            using var doc = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);

            if (doc.PageCount > MaxSourcePages)
            {
                PdfExtractLog.TooManyPages(logger, doc.PageCount);
                return null;
            }

            foreach (var page in doc.Pages)
                page.Rotate = (page.Rotate + delta) % 360;

            using var outStream = new MemoryStream();
            doc.Save(outStream, closeStream: false);
            return outStream.ToArray();
        }
        catch (Exception ex) when (ex is PdfReaderException or InvalidOperationException or IOException or ArgumentException or NotSupportedException)
        {
            PdfExtractLog.ExtractFailed(logger, ex.Message);
            return null;
        }
    }

    public byte[]? ExtractPages(ReadOnlyMemory<byte> pdf, IReadOnlyList<int> pages)
    {
        try
        {
            using var sourceStream = new MemoryStream(pdf.ToArray(), writable: false);
            using var source = PdfReader.Open(sourceStream, PdfDocumentOpenMode.Import);

            var total = source.PageCount;
            if (total > MaxSourcePages)
            {
                PdfExtractLog.TooManyPages(logger, total);
                return null;
            }

            // Páginas base 1 → índices base 0; filtra fuera de rango, dedup y limita a 20 (paridad de seguridad).
            var indices = pages
                .Select(p => p - 1)
                .Where(i => i >= 0 && i < total)
                .Distinct()
                .Take(MaxExtractedPages)
                .ToList();

            if (indices.Count == 0)
                return null;

            using var target = new PdfDocument();
            foreach (var i in indices)
                target.AddPage(source.Pages[i]);

            using var outStream = new MemoryStream();
            target.Save(outStream, closeStream: false);
            return outStream.ToArray();
        }
        catch (Exception ex) when (ex is PdfReaderException or InvalidOperationException or IOException or ArgumentException or NotSupportedException)
        {
            PdfExtractLog.ExtractFailed(logger, ex.Message);
            return null;
        }
    }
}

/// <summary>Logging source-generated (CA1848) del extractor de páginas. No loguea el contenido del PDF.</summary>
internal static partial class PdfExtractLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Extracción PDF omitida: el documento tiene {Pages} páginas (máx 200)")]
    public static partial void TooManyPages(ILogger logger, int pages);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo recortar el PDF: {Detail}")]
    public static partial void ExtractFailed(ILogger logger, string detail);
}
