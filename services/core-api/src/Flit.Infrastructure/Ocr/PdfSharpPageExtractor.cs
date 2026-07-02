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
