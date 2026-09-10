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
                page.Size(PageSizes.A4);
                page.Margin(1.4f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(9).FontFamily(Fonts.Arial).FontColor(Hex(Ink)));

                page.Header().Column(col =>
                {
                    col.Item().Text(txt => txt.Span(kicker).FontSize(9).FontColor(Hex(Muted)));
                    col.Item().Text(txt => txt.Span(title).Bold().FontSize(15));
                    col.Item().Text($"Periodo: {periodLabel}").FontSize(9).FontColor(Hex(Muted));
                    col.Item().PaddingTop(4).LineHorizontal(0.75f).LineColor(Hex(Hairline));
                });

                page.Content().PaddingVertical(8).Column(col =>
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

                page.Footer().AlignRight().Text(txt =>
                {
                    txt.Span("Generado por FLIT · ").FontSize(7).FontColor(Hex(Muted));
                    txt.Span(periodLabel).FontSize(7).FontColor(Hex(Muted));
                });
            });
        }).GeneratePdf();
    }

    private static Color Hex(string hex) => Color.FromHex(hex);

    private static void Th(TableDescriptor table, string label) =>
        table.Cell().Background(Hex(Track)).Border(0.5f).BorderColor(Hex(Hairline)).Padding(3)
            .Text(txt => txt.Span(label).FontSize(8).Bold());

    private static void Td(TableDescriptor table, string value) =>
        table.Cell().Border(0.5f).BorderColor(Hex(Hairline)).Padding(3)
            .Text(string.IsNullOrWhiteSpace(value) ? "-" : value).FontSize(8);
}
