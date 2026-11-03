using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Advanced;
using PdfSharpCore.Pdf.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// HU #14209 — normaliza PDFs que PdfSharp no puede abrir en <see cref="PdfDocumentOpenMode.Modify"/>
/// (estampado de pie). N1 reescribe vía Import; N2 reconstruye desde imágenes embebidas (escaneos).
/// HU #14240 — si tampoco abre en Import, sustituye por una página de aviso generada con QuestPDF
/// (siempre Import-ok) para no tumbar el Merge del consolidado.
/// </summary>
internal static class PdfModifyNormalizer
{
    /// <summary>
    /// True si PdfSharp puede abrir el PDF en <see cref="PdfDocumentOpenMode.Import"/> (requisito de Merge).
    /// </summary>
    public static bool CanOpenForImport(byte[] pdf)
    {
        if (pdf is not { Length: > 4 })
            return false;

        try
        {
            using var inputStream = new MemoryStream(pdf, writable: false);
            using var input = PdfReader.Open(inputStream, PdfDocumentOpenMode.Import);
            return input.PageCount > 0;
        }
        catch (Exception ex) when (IsRecoverablePdfException(ex))
        {
            return false;
        }
    }

    /// <summary>
    /// Devuelve <paramref name="pdf"/> si Import funciona; si no, una página de aviso Import-ok.
    /// </summary>
    public static byte[] EnsureImportable(byte[] pdf, string? documentName, out bool substituted)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        if (CanOpenForImport(pdf))
        {
            substituted = false;
            return pdf;
        }

        substituted = true;
        return CreateIncompatiblePlaceholder(documentName);
    }

    /// <summary>
    /// Página de aviso (QuestPDF) cuando el adjunto no es legible por PdfSharp. Siempre Import-ok.
    /// </summary>
    public static byte[] CreateIncompatiblePlaceholder(string? documentName)
    {
        var label = string.IsNullOrWhiteSpace(documentName)
            ? "Documento adjunto"
            : documentName.Trim();

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(48);
                page.DefaultTextStyle(x => x.FontSize(11));
                page.Content().Column(col =>
                {
                    col.Spacing(12);
                    col.Item().Text("Documento no incluido en el consolidado").Bold().FontSize(14);
                    col.Item().Text(label).SemiBold();
                    col.Item().Text(
                        "El archivo PDF no pudo procesarse (formato interno incompatible con el consolidador). " +
                        "Reemplace el adjunto regenerándolo: abra el PDF, use Imprimir → Guardar como PDF, " +
                        "y vuelva a cargarlo en el trámite.");
                });
            });
        }).GeneratePdf();
    }

    /// <summary>
    /// N1: abre en Import, copia páginas a un documento nuevo y guarda. Produce un PDF más simple
    /// que suele aceptar Modify (equivalente ligero a «imprimir → guardar como PDF»).
    /// </summary>
    public static bool TryRewriteViaImport(byte[] pdf, out byte[] rewritten)
    {
        rewritten = [];
        if (pdf is not { Length: > 4 })
            return false;

        try
        {
            using var inputStream = new MemoryStream(pdf, writable: false);
            using var input = PdfReader.Open(inputStream, PdfDocumentOpenMode.Import);
            if (input.PageCount == 0)
                return false;

            using var output = new PdfDocument();
            for (var i = 0; i < input.PageCount; i++)
                output.AddPage(input.Pages[i]);

            using var ms = new MemoryStream();
            output.Save(ms, false);
            rewritten = ms.ToArray();
            return rewritten.Length > 4;
        }
        catch (Exception ex) when (IsRecoverablePdfException(ex))
        {
            return false;
        }
    }

    /// <summary>
    /// N2: si cada página tiene al menos un XObject /Image decodificable, reconstruye un PDF
    /// rasterizado con QuestPDF (última opción; pierde texto seleccionable).
    /// </summary>
    public static bool TryRasterizeFromPageImages(byte[] pdf, out byte[] rasterized)
    {
        rasterized = [];
        if (pdf is not { Length: > 4 })
            return false;

        try
        {
            using var inputStream = new MemoryStream(pdf, writable: false);
            using var input = PdfReader.Open(inputStream, PdfDocumentOpenMode.Import);
            if (input.PageCount == 0)
                return false;

            var pageImages = new List<byte[]>(input.PageCount);
            foreach (PdfPage page in input.Pages)
            {
                var largest = TryLargestPageImagePng(page);
                if (largest is null)
                    return false;
                pageImages.Add(largest);
            }

            rasterized = ImagesToPdf(pageImages);
            return rasterized.Length > 4;
        }
        catch (Exception ex) when (IsRecoverablePdfException(ex))
        {
            return false;
        }
    }

    internal static bool IsRecoverablePdfException(Exception ex) =>
        ex is PdfReaderException
            or InvalidOperationException
            or IOException
            or ArgumentException
            or NotSupportedException
            or FormatException;

    private static byte[]? TryLargestPageImagePng(PdfPage page)
    {
        byte[]? best = null;
        foreach (var dict in EnumerateImageDicts(page))
        {
            var png = PdfXObjectPngDecoder.TryDecode(dict);
            if (png is not { Length: > 0 })
                continue;
            if (best is null || png.Length > best.Length)
                best = png;
        }

        return best;
    }

    private static IEnumerable<PdfDictionary> EnumerateImageDicts(PdfPage page)
    {
        var resources = ResolveDict(page.Elements["/Resources"]);
        foreach (var dict in EnumerateFromResources(resources, depth: 0))
            yield return dict;
    }

    private static IEnumerable<PdfDictionary> EnumerateFromResources(PdfDictionary? resources, int depth)
    {
        if (resources is null || depth > 8)
            yield break;

        var xObjects = ResolveDict(resources.Elements["/XObject"]);
        if (xObjects is null)
            yield break;

        foreach (var item in xObjects.Elements.Values)
        {
            var dict = ResolveDict(item);
            if (dict is null)
                continue;

            if (IsImageXObject(dict))
                yield return dict;
            else if (IsFormXObject(dict))
            {
                var formResources = ResolveDict(dict.Elements["/Resources"]);
                foreach (var nested in EnumerateFromResources(formResources, depth + 1))
                    yield return nested;
            }
        }
    }

    private static PdfDictionary? ResolveDict(PdfItem? item) =>
        item as PdfDictionary ?? (item as PdfReference)?.Value as PdfDictionary;

    private static bool IsImageXObject(PdfDictionary dict) => SubtypeContains(dict, "/Image");

    private static bool IsFormXObject(PdfDictionary dict) => SubtypeContains(dict, "/Form");

    private static bool SubtypeContains(PdfDictionary dict, string token)
    {
        var subtype = dict.Elements["/Subtype"]?.ToString();
        return subtype is not null && subtype.Contains(token, StringComparison.Ordinal);
    }

    private static byte[] ImagesToPdf(IReadOnlyList<byte[]> pageImages) =>
        Document.Create(container =>
        {
            foreach (var image in pageImages)
            {
                container.Page(page =>
                {
                    page.Margin(0);
                    page.Content().Image(image).FitArea();
                });
            }
        }).GeneratePdf();
}
