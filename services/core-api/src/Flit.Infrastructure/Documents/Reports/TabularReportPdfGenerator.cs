using Flit.Infrastructure.Documents.Branding;
using QuestPDF;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Flit.Infrastructure.Documents.Reports;

/// <summary>
/// Generador genérico de un PDF de varias secciones tabulares (Reportes 2.0, HU-D — mismo caso de
/// uso que <see cref="TabularWorkbookWriter"/>, en PDF): cada colección del DTO agregado es una
/// tabla con título propio. Sin gráficas ni KPIs destacados (eso es lo que hace
/// <c>ExecutiveSummaryPdfGenerator</c> para "Resumen"): aquí el objetivo es que TODA la información
/// del tab llegue al correo, no una versión editorializada.
/// </summary>
internal static class TabularReportPdfGenerator
{
    private const string Ink = "#162744";
    private const string Muted = "#59677D";
    private const string Track = "#EEF1F6";
    private const string Hairline = "#DFE5ED";

    public sealed record Section(
        string Title, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows, string? EmptyMessage = null);

    static TabularReportPdfGenerator()
    {
        Settings.License = LicenseType.Community;
    }

    public static byte[] Generate(string kicker, string title, string periodLabel, IReadOnlyList<Section> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                var landscape = sections.Any(x => x.Headers.Count > MaxPortraitColumns);
                ConfigurePage(page, landscape, header =>
                {
                    header.Item().Text(txt => txt.Span(kicker).FontSize(9).FontColor(Hex(Muted)));
                    header.Item().Text(txt => txt.Span(title).Bold().FontSize(15));
                    header.Item().Text($"Periodo: {periodLabel}").FontSize(9).FontColor(Hex(Muted));
                }, footer => footer.Span(periodLabel));

                page.Content().PaddingHorizontal(FlitDocumentTheme.MarginCm, Unit.Centimetre).PaddingVertical(8).Column(col =>
                {
                    col.Spacing(14);
                    foreach (var section in sections)
                    {
                        col.Item().Text(txt => txt.Span(section.Title).Bold().FontSize(11).FontColor(Hex(Ink)));

                        if (section.Rows.Count == 0)
                        {
                            col.Item().Text(section.EmptyMessage ?? "Sin datos en el periodo seleccionado.")
                                .FontColor(Hex(Muted));
                            continue;
                        }

                        col.Item().Table(t =>
                        {
                            t.ColumnsDefinition(c =>
                            {
                                foreach (var _ in section.Headers)
                                    c.RelativeColumn();
                            });

                            foreach (var header in section.Headers)
                                Th(t, header);

                            foreach (var row in section.Rows)
                                foreach (var cell in row)
                                    Td(t, cell);
                        });
                    }
                });
            });
        }).GeneratePdf();
    }

    /// <summary>
    /// Columnas que caben en Carta vertical con Poppins 8 pt sin partir palabras de encabezado (p. ej. «Reincidencia (%)»).
    /// Si alguna tabla tiene más, el reporte entero sale en Carta horizontal.
    /// </summary>
    internal const int MaxPortraitColumns = 6;

    /// <summary>Alto (pt) desde el borde inferior de la hoja hasta el texto del pie, sobre la banda del membrete.</summary>
    private const float FooterTextFromBottom = 30;

    /// <summary>
    /// Armazón FLIT de los reportes en PDF (Correos 2.0): Carta, sin margen y Poppins como <see cref="FlitLetterhead.ApplyTo"/>,
    /// pero con el encabezado y el pie compuestos aquí, porque el título del reporte y «Generado por FLIT» se repiten en cada
    /// página (ApplyTo ocupa esas dos ranuras solo con las bandas). Arriba: banda del membrete, luego el bloque de título con el
    /// margen FLIT. Abajo: banda del membrete con el texto del pie encima, alineado a la derecha. En horizontal (tablas anchas)
    /// las bandas, dibujadas para Carta vertical, se ajustan al área sin estirarse (<c>FitArea</c>), como el reporte de catálogo
    /// de core-diagnostico.
    /// </summary>
    internal static void ConfigurePage(
        PageDescriptor page, bool landscape, Action<ColumnDescriptor> titleBlock, Action<TextDescriptor> footerSuffix)
    {
        page.Size(landscape ? FlitDocumentTheme.Page.Landscape() : FlitDocumentTheme.Page);
        page.Margin(0);
        page.DefaultTextStyle(t => t.FontSize(9).FontFamily(FlitDocumentTheme.FontRegular).FontColor(Hex(Ink)));

        page.Header().Column(col =>
        {
            if (landscape)
                col.Item().Height(FlitDocumentTheme.MarginCm, Unit.Centimetre).AlignTop().AlignCenter().Svg(BrandingAssets.MembreteHeaderSvg).FitArea();
            else
                col.Item().Element(FlitLetterhead.Header);
            col.Item().PaddingHorizontal(FlitDocumentTheme.MarginCm, Unit.Centimetre).Column(block =>
            {
                titleBlock(block);
                block.Item().PaddingTop(4).LineHorizontal(0.75f).LineColor(Hex(Hairline));
            });
        });

        page.Footer().Layers(layers =>
        {
            if (landscape)
                layers.PrimaryLayer().Height(FlitDocumentTheme.MarginCm, Unit.Centimetre).AlignBottom().AlignCenter().Svg(BrandingAssets.MembreteFooterSvg).FitArea();
            else
                layers.PrimaryLayer().Element(FlitLetterhead.Footer);
            layers.Layer().AlignBottom().PaddingBottom(FooterTextFromBottom)
                .PaddingRight(FlitDocumentTheme.MarginCm, Unit.Centimetre).AlignRight().Text(txt =>
                {
                    txt.DefaultTextStyle(t => t.FontSize(7).FontColor(Hex(Muted)));
                    txt.Span("Generado por FLIT · ");
                    footerSuffix(txt);
                });
        });
    }

    private static Color Hex(string hex) => Color.FromHex(hex);

    private static void Th(TableDescriptor table, string label) =>
        table.Cell().Background(Hex(Track)).Border(0.5f).BorderColor(Hex(Hairline)).Padding(3)
            .Text(txt => txt.Span(label).FontSize(8).Bold());

    private static void Td(TableDescriptor table, string value) =>
        table.Cell().Border(0.5f).BorderColor(Hex(Hairline)).Padding(3)
            .Text(string.IsNullOrWhiteSpace(value) ? "-" : value).FontSize(8);
}
