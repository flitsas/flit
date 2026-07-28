using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Flit.Infrastructure.Documents.Branding;

/// <summary>Datos de la portada del expediente (HU #10857). Valores vacíos se pintan como marcador seguro.</summary>
public sealed record FlitCoverData(
    string CodigoTramite,
    string Placa,
    string TipoTramite,
    string SecretariaTransito,
    string CompaniaRadicadora);

/// <summary>
/// Genera la portada institucional del expediente consolidado (HU #10857, punto 3) en tamaño Carta,
/// según la muestra oficial (recursos dllo membrete): banda de membrete superior, logo FLIT con
/// "Versión 2.0" centrado, líneas divisoras en gradiente que enmarcan la etiqueta "TRÁMITE:" y el
/// código del trámite (Poppins Bold, #557EFF), y el bloque de datos del trámite centrado (etiquetas
/// en Bold #162744, valores en Medium). El compositor del consolidado la antepone como primera página.
/// </summary>
public static class FlitCoverPageGenerator
{
    static FlitCoverPageGenerator()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
    }

    public static byte[] Generate(FlitCoverData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        FlitFonts.EnsureRegistered();

        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(FlitDocumentTheme.Page);
                page.Margin(0);
                page.DefaultTextStyle(t => t.FontFamily(FlitDocumentTheme.FontRegular).FontColor(FlitDocumentTheme.DarkNavy));

                // Bandas de membrete a ancho completo (arriba y abajo).
                page.Header().Svg(BrandingAssets.PortadaHeaderSvg);
                page.Footer().Svg(BrandingAssets.MembreteFooterSvg);

                page.Content()
                    .PaddingHorizontal(FlitDocumentTheme.MarginCm, Unit.Centimetre)
                    .Column(col =>
                    {
                        // Logo FLIT + "Versión 2.0" centrado, en la zona media-superior.
                        col.Item().PaddingTop(3.5f, Unit.Centimetre).AlignCenter().Width(230).Svg(BrandingAssets.PortadaLogoSvg);

                        // Etiqueta "TRÁMITE:" y código del trámite entre dos líneas divisoras.
                        col.Item().PaddingTop(1.6f, Unit.Centimetre).AlignCenter().Width(360).Svg(BrandingAssets.PortadaDividerSvg);
                        col.Item().PaddingTop(10).AlignCenter().Text("TRÁMITE:")
                            .Bold().FontSize(12).FontColor(FlitDocumentTheme.DarkNavy);
                        col.Item().PaddingTop(2).AlignCenter().Text(Val(data.CodigoTramite))
                            .Bold().FontSize(26).FontColor(FlitDocumentTheme.PrimaryBlue);
                        col.Item().PaddingTop(10).AlignCenter().Width(360).Svg(BrandingAssets.PortadaDividerSvg);

                        // Datos del trámite, centrados (etiqueta Bold + valor Medium).
                        col.Item().PaddingTop(1.4f, Unit.Centimetre).Column(info =>
                        {
                            info.Spacing(6);
                            Field(info, "Placa", data.Placa);
                            Field(info, "Tipo de trámite", data.TipoTramite);
                            Field(info, "Secretaría de Tránsito", data.SecretariaTransito);
                            Field(info, "Compañía radicadora", data.CompaniaRadicadora);
                        });
                    });
            });
        }).GeneratePdf();
    }

    private static void Field(ColumnDescriptor column, string label, string? value) =>
        column.Item().AlignCenter().Text(text =>
        {
            text.Span($"{label}: ")
                .FontFamily(FlitDocumentTheme.FontRegular).Bold()
                .FontSize(12).FontColor(FlitDocumentTheme.DarkNavy);
            text.Span(Val(value))
                .FontFamily(FlitDocumentTheme.FontMedium)
                .FontSize(12).FontColor(FlitDocumentTheme.DarkNavy);
        });

    // Datos incompletos → marcador seguro ('-'); nunca vacío, nunca excepción.
    private static string Val(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}
