using Flit.Tramites.Application.Identity;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Advanced;
using PdfSharpCore.Pdf.IO;
using SixLabors.ImageSharp;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Recorte de la rúbrica del certificado Kyverum a partir de XObject /Image (ADR-0054).
/// Si el PDF está aplanado y no hay imagen decodificable a PNG, devuelve null.
/// </summary>
internal sealed class IdentitySignatureExtractor : IIdentitySignatureExtractor
{
    public IdentitySignatureCrop? TryExtract(byte[] pdfBytes)
    {
        if (pdfBytes is not { Length: > 4 })
            return null;

        try
        {
            using var input = new MemoryStream(pdfBytes, writable: false);
            using var doc = PdfReader.Open(input, PdfDocumentOpenMode.Import);
            Candidate? bestSignature = null;
            Candidate? bestAny = null;

            foreach (PdfPage page in doc.Pages)
            {
                foreach (var dict in EnumerateImageDicts(page))
                {
                    var png = PdfXObjectPngDecoder.TryDecode(dict);
                    if (png is not { Length: > 0 } || png.Length > 400_000)
                        continue;
                    if (!TryMeasure(png, out var width, out var height))
                        continue;

                    var candidate = new Candidate(
                        png,
                        width,
                        height,
                        PdfXObjectPngDecoder.HasTransparencyMask(dict),
                        PdfXObjectPngDecoder.CountVisibleColors(png, Candidate.LowPaletteMax));
                    if (bestAny is null || candidate.Beats(bestAny))
                        bestAny = candidate;
                    if (candidate.LooksLikeSignature && (bestSignature is null || candidate.Beats(bestSignature)))
                        bestSignature = candidate;
                }
            }

            var best = bestSignature ?? bestAny;
            if (best is null)
                return null;

            var ink = PdfXObjectPngDecoder.ToDocumentInk(best.Png);
            return PdfXObjectPngDecoder.LooksLikeSignatureArtifact(ink) ? new IdentitySignatureCrop(ink) : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Bug #13304 — además de tinta visible exige fondo transparente y paleta baja: el logo «Verify»
    /// persistido por error deja de valer y la captura lo re-extrae (autocorrección).
    /// </summary>
    public bool IsUsableInk(byte[] imageBytes) =>
        PdfXObjectPngDecoder.LooksLikeSignatureArtifact(imageBytes);

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
            {
                yield return dict;
                continue;
            }

            if (IsFormXObject(dict))
            {
                var inner = ResolveDict(dict.Elements["/Resources"]);
                foreach (var nested in EnumerateFromResources(inner, depth + 1))
                    yield return nested;
            }
        }
    }

    private static PdfDictionary? ResolveDict(PdfItem? item) =>
        item as PdfDictionary ?? (item as PdfReference)?.Value as PdfDictionary;

    private static bool IsImageXObject(PdfDictionary dict) =>
        SubtypeContains(dict, "Image");

    private static bool IsFormXObject(PdfDictionary dict) =>
        SubtypeContains(dict, "Form");

    private static bool SubtypeContains(PdfDictionary dict, string token)
    {
        var subtype = dict.Elements.GetString("/Subtype")
                      ?? dict.Elements.GetName("/Subtype");
        return !string.IsNullOrWhiteSpace(subtype)
               && subtype.Contains(token, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryMeasure(byte[] png, out int width, out int height)
    {
        width = 0;
        height = 0;
        try
        {
            var info = Image.Identify(png);
            if (info is null || info.Width < 8 || info.Height < 8)
                return false;
            width = info.Width;
            height = info.Height;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Bug #13304 — señal principal: transparencia (/SMask o /Mask) y luego paleta baja; aspecto y área
    /// quedan como desempate. Kyverum añadió un logo de cabecera 676x200 opaco y de color continuo que,
    /// por aspecto, le ganaba a la rúbrica 672x270 con /SMask.
    /// </summary>
    private sealed record Candidate(byte[] Png, int Width, int Height, bool HasMask, int Colors)
    {
        public const int LowPaletteMax = 256;

        private int Score => (HasMask ? 2 : 0) + (Colors <= LowPaletteMax ? 1 : 0);

        public int Area => Width * Height;

        public double Aspect => Height == 0 ? 0 : (double)Width / Height;

        /// <summary>Descarta fotos de cédula (muy grandes) y QR/logo (casi cuadrados y chicos).</summary>
        public bool LooksLikeSignature =>
            Width >= 200 && Height >= 40 && Height < Width && Area is >= 8_000 and <= 400_000;

        public bool Beats(Candidate other) =>
            Score != other.Score
                ? Score > other.Score
                : Aspect > other.Aspect || (Math.Abs(Aspect - other.Aspect) <= 0.15 && Area < other.Area);
    }
}
