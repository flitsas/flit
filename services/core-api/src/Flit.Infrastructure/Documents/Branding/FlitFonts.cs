using PdfSharpCore.Fonts;
using QuestPDF.Drawing;

namespace Flit.Infrastructure.Documents.Branding;

/// <summary>
/// Registro idempotente de la tipografía de marca FLIT (HU #10855) para los dos motores de PDF:
/// <list type="bullet">
///   <item>QuestPDF (portada, certificados) — <see cref="FontManager.RegisterFont(Stream)"/>.</item>
///   <item>PdfSharpCore (stamper de pie/marca de agua, overlay del FUR) — <see cref="GlobalFontSettings.FontResolver"/>
///     con el resolutor superset <see cref="FlitFontResolver"/>.</item>
/// </list>
/// Debe ejecutarse antes de la primera generación/estampado. Idempotente y thread-safe.
/// </summary>
public static class FlitFonts
{
    private static readonly string[] QuestPdfFontResources =
    [
        "Flit.Infrastructure.Documents.Branding.Fonts.Poppins-Regular.ttf",
        "Flit.Infrastructure.Documents.Branding.Fonts.Poppins-Medium.ttf",
        "Flit.Infrastructure.Documents.Branding.Fonts.Poppins-Bold.ttf",
    ];

    private static int _registered;

    /// <summary>Registra Poppins en QuestPDF y fija el resolutor superset de PdfSharpCore. Una sola vez.</summary>
    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) != 0)
            return;

        var asm = typeof(FlitFonts).Assembly;
        foreach (var resource in QuestPdfFontResources)
        {
            using var stream = asm.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Fuente embebida no encontrada: {resource}");
            FontManager.RegisterFont(stream);
        }

        // Único slot global de PdfSharpCore: el resolutor superset sirve Poppins y cae a DejaVu.
        GlobalFontSettings.FontResolver = new FlitFontResolver();
    }
}
