using System.Collections.Concurrent;
using PdfSharpCore.Fonts;

namespace Flit.Infrastructure.Documents.Branding;

/// <summary>
/// Resolutor de fuentes único para PdfSharpCore (HU #10855). PdfSharpCore expone un solo
/// <see cref="GlobalFontSettings.FontResolver"/> global en el proceso, compartido por el overlay
/// del FUR (HU #10256) y por el stamper de marca (pie/marca de agua). Este resolutor es un
/// <b>superset</b>: sirve las fuentes Poppins embebidas para la marca FLIT y cae a DejaVu Sans
/// para cualquier otra familia (p. ej. "Arial" que dibuja el overlay del FUR), preservando el
/// comportamiento previo. Así no importa qué ruta registre primero: el resolutor satisface a ambas.
/// </summary>
public sealed class FlitFontResolver : IFontResolver
{
    private const string PoppinsRegularFace = "Poppins#Regular";
    private const string PoppinsMediumFace = "Poppins#Medium";
    private const string PoppinsBoldFace = "Poppins#Bold";
    private const string DejaVuRegularFace = "DejaVuSans";
    private const string DejaVuBoldFace = "DejaVuSans-Bold";

    private static readonly Dictionary<string, string> FaceResources = new(StringComparer.Ordinal)
    {
        [PoppinsRegularFace] = "Flit.Infrastructure.Documents.Branding.Fonts.Poppins-Regular.ttf",
        [PoppinsMediumFace] = "Flit.Infrastructure.Documents.Branding.Fonts.Poppins-Medium.ttf",
        [PoppinsBoldFace] = "Flit.Infrastructure.Documents.Branding.Fonts.Poppins-Bold.ttf",
        [DejaVuRegularFace] = "Flit.Infrastructure.Documents.Fur.Fonts.DejaVuSans.ttf",
        [DejaVuBoldFace] = "Flit.Infrastructure.Documents.Fur.Fonts.DejaVuSans-Bold.ttf",
    };

    private static readonly ConcurrentDictionary<string, byte[]> Cache = new(StringComparer.Ordinal);

    /// <summary>Familia por defecto (cae a DejaVu Sans, como el resolutor previo del FUR).</summary>
    public string DefaultFontName => DejaVuRegularFace;

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var family = familyName?.Trim() ?? string.Empty;

        if (family.StartsWith("Poppins", StringComparison.OrdinalIgnoreCase))
        {
            if (family.Contains("Medium", StringComparison.OrdinalIgnoreCase))
                return new FontResolverInfo(PoppinsMediumFace);
            return new FontResolverInfo(isBold ? PoppinsBoldFace : PoppinsRegularFace);
        }

        // Cualquier otra familia (incl. "Arial" del overlay del FUR) → DejaVu Sans, como antes.
        return new FontResolverInfo(isBold ? DejaVuBoldFace : DejaVuRegularFace);
    }

    public byte[] GetFont(string faceName) => Cache.GetOrAdd(faceName, LoadFontBytes);

    private static byte[] LoadFontBytes(string faceName)
    {
        var resource = FaceResources.TryGetValue(faceName, out var res) ? res : FaceResources[DejaVuRegularFace];
        var asm = typeof(FlitFontResolver).Assembly;
        using var stream = asm.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Fuente embebida no encontrada: {resource}");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
