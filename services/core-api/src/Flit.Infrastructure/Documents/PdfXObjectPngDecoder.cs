using System.IO.Compression;
using System.Linq;
using Flit.Tramites.Application.Identity;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Advanced;
using PdfSharpCore.Pdf.Filters;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Convierte un XObject /Image a PNG de archivo. PdfSharpCore entrega el stream filtrado (JPEG DCT
/// o píxeles Flate), no un PNG listo para <c>XImage.FromStream</c>.
/// </summary>
internal static class PdfXObjectPngDecoder
{
    private static readonly FlateDecode Flate = new();
    private static readonly PngEncoder Png = new() { ColorType = PngColorType.RgbWithAlpha };

    public static byte[]? TryDecode(PdfDictionary dict)
    {
        if (dict.Stream is null)
            return null;

        var raw = dict.Stream.Value;
        if (raw is not { Length: > 0 } || raw.Length > 800_000)
            return null;

        var filter = FilterName(dict);
        if (IdentitySignatureImageFormat.IsJpeg(raw) || filter.Contains("DCTDecode", StringComparison.OrdinalIgnoreCase))
            return ReencodeRasterFile(raw);

        if (IdentitySignatureImageFormat.IsPng(raw))
            return ReencodeRasterFile(raw);

        var pixels = TryUnfilterPixels(raw, filter);
        if (pixels is null)
            return null;

        pixels = ApplyPredictor(dict, pixels);
        if (pixels is null)
            return null;
        return RasterToPng(dict, pixels);
    }

    private static string FilterName(PdfDictionary dict)
    {
        var item = dict.Elements["/Filter"];
        return item switch
        {
            PdfName name => name.Value ?? name.ToString() ?? string.Empty,
            PdfArray array => string.Join(' ', array.Elements.Select(e => e?.ToString() ?? string.Empty)),
            PdfReference reference => reference.Value?.ToString() ?? string.Empty,
            _ => item?.ToString() ?? string.Empty,
        };
    }

    private static byte[]? TryUnfilterPixels(byte[] raw, string filter)
    {
        if (filter.Contains("DCTDecode", StringComparison.OrdinalIgnoreCase)
            || filter.Contains("CCITT", StringComparison.OrdinalIgnoreCase)
            || filter.Contains("JBIG", StringComparison.OrdinalIgnoreCase)
            || filter.Contains("JPX", StringComparison.OrdinalIgnoreCase))
            return null;

        if (string.IsNullOrWhiteSpace(filter) || filter.Contains("Flate", StringComparison.OrdinalIgnoreCase))
        {
            var inflated = Inflate(raw);
            if (inflated is { Length: > 0 })
                return inflated;
            if (string.IsNullOrWhiteSpace(filter))
                return raw;
        }

        return null;
    }

    private static byte[]? Inflate(byte[] data)
    {
        try
        {
            var decoded = Flate.Decode(data, decodeParms: null!);
            if (decoded is { Length: > 0 } && !IsDecodeErrorBanner(decoded))
                return decoded;
        }
        catch (Exception)
        {
            // PdfSharpCore a veces no infla Flate de imágenes; se reintenta con zlib/.NET.
        }

        try
        {
            using var input = new MemoryStream(data, writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            var bytes = output.ToArray();
            return bytes.Length > 0 ? bytes : null;
        }
        catch (Exception)
        {
            // encabezado zlib ausente
        }

        if (data.Length > 6)
        {
            try
            {
                using var input = new MemoryStream(data, 2, data.Length - 6, writable: false);
                using var deflate = new DeflateStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                deflate.CopyTo(output);
                var bytes = output.ToArray();
                return bytes.Length > 0 ? bytes : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        return null;
    }

    private static bool IsDecodeErrorBanner(byte[] bytes)
    {
        if (bytes.Length is < 10 or > 80)
            return false;
        var text = System.Text.Encoding.Latin1.GetString(bytes);
        return text.Contains("Cannot decode filter", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Can't decode", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[]? ApplyPredictor(PdfDictionary dict, byte[] pixels)
    {
        var parms = dict.Elements["/DecodeParms"] as PdfDictionary
                    ?? (dict.Elements["/DecodeParms"] as PdfReference)?.Value as PdfDictionary;
        var predictor = parms?.Elements.GetInteger("/Predictor") ?? 1;
        var width = dict.Elements.GetInteger("/Width");
        var height = dict.Elements.GetInteger("/Height");
        var components = ComponentCount(dict);
        if (width <= 0 || height <= 0 || components <= 0)
            return pixels;

        var row = RowBytes(width, components, BitsPerComponent(dict));
        if (row <= 0)
            return pixels;

        if (predictor is >= 10 and <= 15)
            return UnfilterPng(pixels, height, row, components);
        if (predictor == 2)
            return UnfilterTiff(pixels, height, row, components);
        return pixels;
    }

    private static int BitsPerComponent(PdfDictionary dict)
    {
        var bpc = dict.Elements.GetInteger("/BitsPerComponent");
        return bpc > 0 ? bpc : 8;
    }

    private static int ComponentCount(PdfDictionary dict)
    {
        var cs = ColorSpaceName(dict);
        if (cs.Contains("Gray", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (cs.Contains("CMYK", StringComparison.OrdinalIgnoreCase))
            return 4;
        return 3;
    }

    private static string ColorSpaceName(PdfDictionary dict)
    {
        var item = dict.Elements["/ColorSpace"];
        return item switch
        {
            PdfName name => name.Value ?? name.ToString() ?? "/DeviceRGB",
            PdfArray array when array.Elements.Count > 0 => array.Elements[0]?.ToString() ?? "/DeviceRGB",
            PdfReference reference => reference.Value?.ToString() ?? "/DeviceRGB",
            _ => item?.ToString() ?? "/DeviceRGB",
        };
    }

    private static int RowBytes(int width, int components, int bpc) =>
        (width * components * bpc + 7) / 8;

    private static byte[]? UnfilterPng(byte[] data, int height, int row, int components)
    {
        if (data.Length != height * (row + 1))
            return data.Length == height * row ? data : null;

        var output = new byte[height * row];
        var prev = new byte[row];
        for (var y = 0; y < height; y++)
        {
            var src = y * (row + 1);
            var filter = data[src];
            var cur = new byte[row];
            Buffer.BlockCopy(data, src + 1, cur, 0, row);
            ApplyPngFilter(filter, cur, prev, components);
            Buffer.BlockCopy(cur, 0, output, y * row, row);
            prev = cur;
        }

        return output;
    }

    private static void ApplyPngFilter(int filter, byte[] cur, byte[] prev, int bpp)
    {
        bpp = Math.Max(1, bpp);
        for (var i = 0; i < cur.Length; i++)
        {
            var left = i >= bpp ? cur[i - bpp] : (byte)0;
            var up = prev[i];
            var upLeft = i >= bpp ? prev[i - bpp] : (byte)0;
            cur[i] = filter switch
            {
                1 => (byte)(cur[i] + left),
                2 => (byte)(cur[i] + up),
                3 => (byte)(cur[i] + ((left + up) / 2)),
                4 => (byte)(cur[i] + Paeth(left, up, upLeft)),
                _ => cur[i],
            };
        }
    }

    private static byte Paeth(byte a, byte b, byte c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
            return a;
        return pb <= pc ? b : c;
    }

    private static byte[] UnfilterTiff(byte[] data, int height, int row, int components)
    {
        if (data.Length < height * row)
            return data;

        var output = new byte[height * row];
        Buffer.BlockCopy(data, 0, output, 0, height * row);
        for (var y = 0; y < height; y++)
        {
            var offset = y * row;
            for (var i = components; i < row; i++)
                output[offset + i] += output[offset + i - components];
        }

        return output;
    }

    private static byte[]? RasterToPng(PdfDictionary dict, byte[] pixels)
    {
        var width = dict.Elements.GetInteger("/Width");
        var height = dict.Elements.GetInteger("/Height");
        if (width is < 8 or > 2000 || height is < 8 or > 2000)
            return null;
        if (BitsPerComponent(dict) != 8)
            return null;

        var components = ComponentCount(dict);
        var expected = width * height * components;
        if (pixels.Length < expected)
            return null;

        try
        {
            using var image = new Image<Rgba32>(width, height);
            var i = 0;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    image[x, y] = components switch
                    {
                        1 => Gray(pixels[i++]),
                        4 => Cmyk(pixels[i++], pixels[i++], pixels[i++], pixels[i++]),
                        _ => new Rgba32(pixels[i++], pixels[i++], pixels[i++]),
                    };
                }
            }

            using var ms = new MemoryStream();
            image.Save(ms, Png);
            var png = ms.ToArray();
            return IdentitySignatureImageFormat.IsPng(png) ? png : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Rgba32 Gray(byte g) => new(g, g, g);

    private static Rgba32 Cmyk(byte c, byte m, byte y, byte k)
    {
        var kk = 1f - (k / 255f);
        return new Rgba32(
            (byte)(255 * (1f - c / 255f) * kk),
            (byte)(255 * (1f - m / 255f) * kk),
            (byte)(255 * (1f - y / 255f) * kk));
    }

    private static byte[]? ReencodeRasterFile(byte[] bytes)
    {
        try
        {
            using var input = new MemoryStream(bytes, writable: false);
            using var image = Image.Load<Rgba32>(input);
            using var ms = new MemoryStream();
            image.Save(ms, Png);
            var png = ms.ToArray();
            return IdentitySignatureImageFormat.IsPng(png) ? png : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Kyverum entrega la rúbrica en gris muy oscuro sobre negro (p. ej. luma ~26). Un umbral de
    /// "fondo &lt; 40" borra el trazo entero y el FUR solo deja el sello de texto. Aquí el fondo
    /// casi negro pasa a transparente y el trazo se amplifica a tinta oscura.
    /// </summary>
    internal static byte[] ToDocumentInk(byte[] png)
    {
        try
        {
            using var image = Image.Load<Rgba32>(png);
            long sum = 0;
            var total = Math.Max(1, image.Width * image.Height);
            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    var p = image[x, y];
                    sum += p.R + p.G + p.B;
                }
            }

            var mean = sum / (double)(total * 3);
            if (mean >= 140)
                return png;

            const int backgroundMaxLuma = 8;
            const int contrastGain = 14;
            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    var p = image[x, y];
                    var luma = (p.R + p.G + p.B) / 3;
                    if (luma <= backgroundMaxLuma)
                    {
                        image[x, y] = new Rgba32(0, 0, 0, 0);
                    }
                    else
                    {
                        var alpha = (byte)Math.Clamp((luma - backgroundMaxLuma) * contrastGain, 48, 255);
                        image[x, y] = new Rgba32(18, 18, 18, alpha);
                    }
                }
            }

            using var ms = new MemoryStream();
            image.Save(ms, Png);
            var outPng = ms.ToArray();
            return IdentitySignatureImageFormat.IsPng(outPng) ? outPng : png;
        }
        catch (Exception)
        {
            return png;
        }
    }

    /// <summary>
    /// Un PNG válido puede ser un rectángulo negro o un recorte sin tinta. Esos no se estampan.
    /// </summary>
    internal static bool HasVisibleInk(byte[]? bytes)
    {
        if (!IdentitySignatureImageFormat.IsSupported(bytes))
            return false;

        try
        {
            using var image = Image.Load<Rgba32>(bytes);
            var total = image.Width * image.Height;
            if (total < 16)
                return false;

            long lumaSum = 0;
            var opaque = 0;
            var visibleInk = 0;
            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    var p = image[x, y];
                    var luma = (p.R + p.G + p.B) / 3;
                    lumaSum += luma;
                    if (p.A >= 200)
                        opaque++;
                    if (p.A >= 96 && luma <= 90)
                        visibleInk++;
                }
            }

            var mean = lumaSum / (double)total;
            if (opaque > total * 0.85 && mean < 70)
                return false;

            return visibleInk >= Math.Max(20, total / 250);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
