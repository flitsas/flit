using System.Collections.Concurrent;

namespace Flit.Infrastructure.Documents.Branding;

/// <summary>
/// Acceso cacheado a los recursos gráficos de marca embebidos (HU #10855): membrete de las hojas
/// y de la portada. Preferimos SVG (vectorial, decisión D4) con un PNG @72x de respaldo. Los assets
/// viven en <c>Documents/Branding/Assets/</c> como <c>EmbeddedResource</c> (ver csproj).
/// </summary>
public static class BrandingAssets
{
    private const string Prefix = "Flit.Infrastructure.Documents.Branding.Assets.";

    private static readonly ConcurrentDictionary<string, string> SvgCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte[]> PngCache = new(StringComparer.Ordinal);

    // Membrete de hojas (documentos generados por FLIT: certificados, etc.).
    public static string MembreteHeaderSvg => Svg("membrete-hoja-header.svg");
    public static string MembreteFooterSvg => Svg("membrete-hoja-footer.svg");

    // Membrete de la portada (varía respecto al de las hojas).
    public static string PortadaHeaderSvg => Svg("portada-header.svg");
    public static string PortadaFooterSvg => Svg("portada-footer.svg");

    // Portada: logo FLIT con "Versión 2.0" (centrado) y línea divisora en gradiente (HU #10857).
    public static string PortadaLogoSvg => Svg("portada-logo.svg");
    public static string PortadaDividerSvg => Svg("portada-divider.svg");

    /// <summary>Contenido SVG del asset indicado (nombre de archivo con extensión).</summary>
    public static string Svg(string fileName) => SvgCache.GetOrAdd(fileName, LoadText);

    /// <summary>PNG @72x de respaldo del asset indicado (nombre de archivo con extensión).</summary>
    public static byte[] Png(string fileName) => PngCache.GetOrAdd(fileName, LoadBytes);

    private static string LoadText(string fileName)
    {
        using var stream = Open(fileName);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static byte[] LoadBytes(string fileName)
    {
        using var stream = Open(fileName);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static Stream Open(string fileName)
    {
        var resource = Prefix + fileName;
        var asm = typeof(BrandingAssets).Assembly;
        return asm.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Asset de marca embebido no encontrado: {resource}");
    }
}
