using System.Collections.Concurrent;

namespace Flit.Diagnostico.Api.Modulos.Reportes.Marca;

/// <summary>
/// Membrete de hoja FLIT embebido (HU #13848): solo <c>membrete-hoja-header.svg</c> y <c>membrete-hoja-footer.svg</c>, copiados de
/// <c>services/core-api/src/Flit.Infrastructure/Documents/Branding/Assets/</c> (HU #10855; la copia de la clase es de
/// <c>BrandingAssets.cs</c> de esa carpeta). Viven en <c>Modulos/Reportes/Marca/Assets/</c> como <c>EmbeddedResource</c> con
/// nombre lógico <c>Flit.Diagnostico.Api.Marca.{archivo}</c> (ver csproj).
/// </summary>
internal static class BrandingAssets
{
    internal const string Prefijo = "Flit.Diagnostico.Api.Marca.";

    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    public static string MembreteHeaderSvg => Svg("membrete-hoja-header.svg");

    public static string MembreteFooterSvg => Svg("membrete-hoja-footer.svg");

    private static string Svg(string archivo) => Cache.GetOrAdd(archivo, Leer);

    private static string Leer(string archivo)
    {
        using var stream = Abrir(archivo);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal static Stream Abrir(string archivo) =>
        typeof(BrandingAssets).Assembly.GetManifestResourceStream(Prefijo + archivo)
        ?? throw new InvalidOperationException($"Recurso de marca embebido no encontrado: {Prefijo}{archivo}");
}
