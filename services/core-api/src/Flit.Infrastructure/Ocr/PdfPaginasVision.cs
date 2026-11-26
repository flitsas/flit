using Flit.Infrastructure.Documents;
using PDFtoImage;
using SkiaSharp;

namespace Flit.Infrastructure.Ocr;

/// <summary>
/// Convierte las páginas de un PDF en JPEG para los proveedores de visión OpenAI-compatible
/// (OpenAI, Kimi). Esas APIs solo aceptan imágenes en <c>image_url</c>: un
/// <c>data:application/pdf</c> lo rechazan (Moonshot: «unsupported image format»). Anthropic no pasa
/// por aquí porque recibe el PDF tal cual como bloque <c>document</c>.
/// Rasteriza con PDFium (PDFtoImage), igual que <see cref="PdfModifyNormalizer"/>, así que sirve
/// también para escaneos (el caso normal en trámites).
/// </summary>
internal static class PdfPaginasVision
{
    /// <summary>Mismo tope que <see cref="PdfSharpPageExtractor"/>: el flujo de OCR nunca manda más.</summary>
    public const int MaxPaginas = 20;

    /// <summary>150 DPI: el texto de un FUR o una cédula escaneada se lee sin inflar el payload.</summary>
    public const int Dpi = 150;

    /// <summary>Calidad JPEG: a 150 DPI una página carta queda en unos cientos de KB.</summary>
    public const int CalidadJpeg = 85;

    /// <summary>
    /// Devuelve un JPEG por página (como mucho <paramref name="maxPaginas"/>, en orden), o <c>null</c>
    /// si el PDF no se puede leer o los nativos de PDFium no cargan. Nunca lanza por un PDF malo.
    /// </summary>
    public static IReadOnlyList<byte[]>? AJpeg(byte[] pdf, int maxPaginas = MaxPaginas, int dpi = Dpi)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPaginas, 1);

        if (pdf.Length <= 4)
            return null;

        if (!(OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()))
            return null;

        try
        {
            using var input = new MemoryStream(pdf, writable: false);
            var paginas = new List<byte[]>();

            // ToImages es perezoso: Take evita renderizar páginas que no se van a mandar.
            foreach (var bitmap in Conversion.ToImages(input, options: new RenderOptions(Dpi: dpi)).Take(maxPaginas))
            {
                using (bitmap)
                {
                    using var jpeg = bitmap.Encode(SKEncodedImageFormat.Jpeg, CalidadJpeg);
                    if (jpeg is null || jpeg.Size == 0)
                        return null;
                    paginas.Add(jpeg.ToArray());
                }
            }

            return paginas.Count == 0 ? null : paginas;
        }
        catch (Exception ex) when (
            PdfModifyNormalizer.IsRecoverablePdfException(ex)
            || PdfModifyNormalizer.IsNativePdfiumFailure(ex)
            || PdfModifyNormalizer.IsPdfiumLibraryException(ex))
        {
            return null;
        }
    }
}
