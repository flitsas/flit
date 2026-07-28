using System.Collections.Concurrent;
using PdfSharpCore.Fonts;

namespace Flit.Infrastructure.Documents.Fur;

/// <summary>
/// Resolutor de fuentes para PdfSharpCore usado por el overlay del FUR (HU #10256).
/// <para>
/// El renderer dibuja con <c>XFont("Arial", ...)</c>, pero PdfSharpCore no embebe fuentes:
/// resuelve las familias contra las fuentes del sistema operativo. El contenedor runtime
/// (<c>aspnet:10.0-alpine</c>) no tiene ninguna fuente instalada, de modo que el primer
/// <c>DrawString</c>/<c>MeasureString</c> lanzaba y el endpoint respondía <b>HTTP 500</b>.
/// </para>
/// <para>
/// Este resolutor sirve una fuente TrueType embebida (DejaVu Sans, licencia Bitstream Vera/Arev)
/// para cualquier familia solicitada, de forma que la generación del FUR es determinista e
/// independiente de las fuentes del SO (mismo resultado en alpine, en CI y en Windows).
/// </para>
/// </summary>
public sealed class FurFontResolver : IFontResolver
{
    private const string RegularFace = "DejaVuSans";
    private const string BoldFace = "DejaVuSans-Bold";
    private const string RegularResource = "Flit.Infrastructure.Documents.Fur.Fonts.DejaVuSans.ttf";
    private const string BoldResource = "Flit.Infrastructure.Documents.Fur.Fonts.DejaVuSans-Bold.ttf";

    private static readonly ConcurrentDictionary<string, byte[]> Cache = new(StringComparer.Ordinal);
    private static int _registered;

    /// <summary>
    /// Registra el resolutor global de PdfSharpCore una sola vez. Idempotente y thread-safe; debe
    /// ejecutarse antes de la primera generación de un FUR.
    /// <para>
    /// Desde HU #10855 (Feature #10852) delega en <see cref="Branding.FlitFonts.EnsureRegistered"/>:
    /// PdfSharpCore expone un único <see cref="GlobalFontSettings.FontResolver"/> por proceso, y el
    /// resolutor superset <see cref="Branding.FlitFontResolver"/> sirve tanto Poppins (marca FLIT)
    /// como DejaVu Sans para "Arial" (comportamiento previo del overlay del FUR).
    /// </para>
    /// </summary>
    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 0)
            Branding.FlitFonts.EnsureRegistered();
    }

    /// <summary>Fuente por defecto (todas las familias caen a DejaVu Sans).</summary>
    public string DefaultFontName => RegularFace;

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new(isBold ? BoldFace : RegularFace);

    public byte[] GetFont(string faceName) =>
        Cache.GetOrAdd(faceName, LoadFontBytes);

    private static byte[] LoadFontBytes(string faceName)
    {
        var resource = string.Equals(faceName, BoldFace, StringComparison.Ordinal)
            ? BoldResource
            : RegularResource;

        var asm = typeof(FurFontResolver).Assembly;
        using var stream = asm.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Fuente embebida no encontrada: {resource}");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
