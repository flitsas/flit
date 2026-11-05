using QuestPDF.Drawing;

namespace Flit.Diagnostico.Api.Modulos.Reportes.Marca;

/// <summary>
/// Registro idempotente de Poppins Regular, Medium y Bold en QuestPDF (HU #13848). Copia recortada de
/// <c>services/core-api/src/Flit.Infrastructure/Documents/Branding/FlitFonts.cs</c>: solo <see cref="FontManager"/> (aquí no hay
/// PdfSharpCore). Las fuentes son de Google Fonts con licencia SIL OFL 1.1, embebida junto a ellas
/// (<c>Marca/Assets/OFL-LICENSE.txt</c>).
/// </summary>
internal static class FuentesFlit
{
    internal static readonly string[] Archivos = ["Poppins-Regular.ttf", "Poppins-Medium.ttf", "Poppins-Bold.ttf"];

    private static int _registradas;

    /// <summary>Registra las tres fuentes una sola vez por proceso (thread-safe).</summary>
    public static void Registrar()
    {
        if (Interlocked.Exchange(ref _registradas, 1) != 0)
            return;
        foreach (var archivo in Archivos)
        {
            using var stream = BrandingAssets.Abrir(archivo);
            FontManager.RegisterFont(stream);
        }
    }
}
