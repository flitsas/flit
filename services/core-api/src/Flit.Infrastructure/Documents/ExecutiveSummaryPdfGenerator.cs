using System.Globalization;
using Flit.Analytics.Application.Abstractions;
using QuestPDF;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Resumen Ejecutivo en PDF (HU #10246) con QuestPDF: periodo consultado, totales por categoría
/// y Top 5 de radicadores. Recibe los agregados ya consultados (no toca BD).
/// </summary>
internal sealed class ExecutiveSummaryPdfGenerator : IExecutiveSummaryPdfGenerator
{
    static ExecutiveSummaryPdfGenerator()
    {
        Settings.License = LicenseType.Community;
    }

    public byte[] Generate(ExecutiveSummaryData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(9).FontFamily(Fonts.Arial));

                page.Header().Column(col =>
                {
                    col.Item().Text(txt => txt.Span("Resumen Ejecutivo — Dashboard de Trámites").Bold().FontSize(14));
                    col.Item().Text($"Periodo consultado: {data.From:yyyy-MM-dd} a {data.To:yyyy-MM-dd}")
                        .FontSize(9).FontColor(Colors.Grey.Darken2);
                });

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(10);

                    col.Item().Text(txt => txt.Span("Totales por categoría").Bold().FontSize(11));
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3);
                            c.RelativeColumn(1);
                        });
                        Header(t, "Categoría");
                        Header(t, "Total");
                        if (data.Categories.Count == 0)
                        {
                            t.Cell().ColumnSpan(2).Border(0.5f).Padding(3)
                                .Text("Sin trámites en el periodo seleccionado.");
                        }
                        else
                        {
                            foreach (var c in data.Categories)
                            {
                                Value(t, c.Category);
                                Value(t, c.Total.ToString(CultureInfo.InvariantCulture));
                            }
                        }
                    });

                    col.Item().PaddingTop(6).Text(txt => txt.Span("Top 5 radicadores").Bold().FontSize(11));
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                        });
                        Header(t, "Usuario");
                        Header(t, "Enviados");
                        Header(t, "Aprobados");
                        Header(t, "Rechazados");
                        if (data.TopProducers.Count == 0)
                        {
                            t.Cell().ColumnSpan(4).Border(0.5f).Padding(3)
                                .Text("Sin actividad de productividad en el periodo.");
                        }
                        else
                        {
                            foreach (var p in data.TopProducers)
                            {
                                Value(t, p.DisplayName);
                                Value(t, p.SubmittedCount.ToString(CultureInfo.InvariantCulture));
                                Value(t, p.ApprovedCount.ToString(CultureInfo.InvariantCulture));
                                Value(t, p.RejectedCount.ToString(CultureInfo.InvariantCulture));
                            }
                        }
                    });
                });

                page.Footer().AlignRight().Text(txt =>
                {
                    txt.Span("Generado por FLIT · ").FontSize(7).FontColor(Colors.Grey.Medium);
                    txt.Span($"{data.From:yyyy-MM-dd}/{data.To:yyyy-MM-dd}").FontSize(7).FontColor(Colors.Grey.Medium);
                });
            });
        }).GeneratePdf();
    }

    private static void Header(TableDescriptor table, string label) =>
        table.Cell().Background(Colors.Grey.Lighten3).Border(0.5f).Padding(3)
            .Text(txt => txt.Span(label).FontSize(8).Bold());

    private static void Value(TableDescriptor table, string value) =>
        table.Cell().Border(0.5f).Padding(3)
            .Text(string.IsNullOrWhiteSpace(value) ? "-" : value);
}
