using System.Globalization;
using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using QuestPDF;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Resumen Ejecutivo en PDF (HU #10246) con QuestPDF: banda de KPIs, gráficas de barras nativas
/// (distribución por categoría, estados por categoría y Top 5 de radicadores) y las tablas de
/// respaldo. Las gráficas se dibujan con primitivas de QuestPDF (cajas con color y ancho/alto
/// proporcional) — sin dependencias de terceros. Recibe los agregados ya consultados (no toca BD).
/// </summary>
internal sealed class ExecutiveSummaryPdfGenerator : IExecutiveSummaryPdfGenerator
{
    // Paleta de marca FLIT — alineada con el dashboard (frontend _reportes/categories.ts).
    private const string Ink = "#162744";
    private const string Muted = "#59677D";
    private const string Track = "#EEF1F6";
    private const string Hairline = "#DFE5ED";

    static ExecutiveSummaryPdfGenerator()
    {
        Settings.License = LicenseType.Community;
    }

    public byte[] Generate(ExecutiveSummaryData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var categories = data.Categories;
        var totalTramites = categories.Sum(c => c.Total);
        var categoriasActivas = categories.Count(c => c.Total > 0);
        var allStatus = categories.SelectMany(c => c.ByStatus).ToList();
        // Vocabulario de estados N 03 (ADR-0022): entregado/aprobado/rechazado/anulado.
        var enviados = allStatus.Where(s => s.Status is "entregado" or "preparado").Sum(s => s.Count);
        var aprobados = allStatus.Where(s => s.Status == "aprobado").Sum(s => s.Count);
        var rechazados = allStatus.Where(s => s.Status is "rechazado" or "anulado").Sum(s => s.Count);

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.4f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(9).FontFamily(Fonts.Arial).FontColor(Hex(Ink)));

                page.Header().Column(col =>
                {
                    col.Item().Text(txt => txt.Span("Resumen Ejecutivo — Dashboard de Trámites").Bold().FontSize(15));
                    col.Item().Text($"Periodo consultado: {data.From:yyyy-MM-dd} a {data.To:yyyy-MM-dd}")
                        .FontSize(9).FontColor(Hex(Muted));
                    col.Item().PaddingTop(4).LineHorizontal(0.75f).LineColor(Hex(Hairline));
                });

                page.Content().PaddingVertical(8).Column(col =>
                {
                    col.Spacing(12);

                    // ── Banda de KPIs ────────────────────────────────────────────────
                    col.Item().Row(row =>
                    {
                        row.Spacing(6);
                        KpiCard(row.RelativeItem(), "Total trámites", totalTramites, Ink);
                        KpiCard(row.RelativeItem(), "Categorías activas", categoriasActivas, "#557EFF");
                        KpiCard(row.RelativeItem(), "Radicados", enviados, "#557EFF");
                        KpiCard(row.RelativeItem(), "Aprobados", aprobados, "#8CC63F");
                        KpiCard(row.RelativeItem(), "Rechazados", rechazados, "#FF4E00");
                    });

                    // ── Distribución por categoría (barra proporcional + barras) ─────
                    col.Item().Element(c => Section(c, "Distribución por categoría"));
                    if (totalTramites == 0)
                    {
                        col.Item().Text("Sin trámites en el periodo seleccionado.").FontColor(Hex(Muted));
                    }
                    else
                    {
                        // Barra proporcional apilada del total por categoría.
                        col.Item().Height(16).Row(row =>
                        {
                            foreach (var c in categories.Where(c => c.Total > 0))
                                row.RelativeItem(c.Total).Background(Hex(CategoryColor(c.Category)));
                        });
                        // Leyenda de categorías.
                        col.Item().Row(row =>
                        {
                            row.Spacing(10);
                            foreach (var c in categories.Where(c => c.Total > 0))
                                Legend(row.AutoItem(), CategoryColor(c.Category),
                                    $"{CategoryLabel(c.Category)} ({Pct(c.Total, totalTramites)})");
                        });
                        // Barra por categoría con valor y %.
                        col.Item().PaddingTop(4).Column(inner =>
                        {
                            inner.Spacing(5);
                            var maxCat = categories.Max(c => c.Total);
                            foreach (var c in categories)
                                inner.Item().Element(e => LabeledBar(
                                    e, CategoryLabel(c.Category), c.Total, maxCat,
                                    CategoryColor(c.Category), $"{c.Total}  ({Pct(c.Total, totalTramites)})"));
                        });
                    }

                    // ── Estados por categoría (barra apilada por estado) ─────────────
                    if (categories.Any(c => c.ByStatus.Sum(s => s.Count) > 0))
                    {
                        col.Item().Element(c => Section(c, "Estados por categoría"));
                        col.Item().Column(inner =>
                        {
                            inner.Spacing(6);
                            foreach (var c in categories.Where(c => c.ByStatus.Sum(s => s.Count) > 0))
                            {
                                inner.Item().Row(row =>
                                {
                                    row.ConstantItem(80).AlignMiddle().Text(CategoryLabel(c.Category)).FontSize(8);
                                    row.RelativeItem().AlignMiddle().Height(12).Row(bar =>
                                    {
                                        foreach (var s in c.ByStatus.Where(s => s.Count > 0))
                                            bar.RelativeItem(s.Count).Background(Hex(StatusColor(s.Status)));
                                    });
                                });
                            }
                            // Leyenda de estados presentes.
                            var statuses = allStatus.GroupBy(s => s.Status)
                                .Where(g => g.Sum(x => x.Count) > 0)
                                .Select(g => g.Key);
                            inner.Item().PaddingTop(2).Row(row =>
                            {
                                row.Spacing(10);
                                foreach (var st in statuses)
                                    Legend(row.AutoItem(), StatusColor(st), StatusLabel(st));
                            });
                        });
                    }

                    // ── Top 5 radicadores (barras horizontales) ──────────────────────
                    col.Item().Element(c => Section(c, "Top 5 radicadores"));
                    if (data.TopProducers.Count == 0)
                    {
                        col.Item().Text("Sin actividad de productividad en el periodo.").FontColor(Hex(Muted));
                    }
                    else
                    {
                        col.Item().Column(inner =>
                        {
                            inner.Spacing(5);
                            var maxSub = Math.Max(1, data.TopProducers.Max(p => p.SubmittedCount));
                            foreach (var p in data.TopProducers)
                                inner.Item().Element(e => LabeledBar(
                                    e, p.DisplayName, p.SubmittedCount, maxSub, "#557EFF",
                                    $"Rad {p.SubmittedCount} · Apr {p.ApprovedCount} · Rec {p.RejectedCount}"));
                        });
                    }

                    // ── Tablas de respaldo ───────────────────────────────────────────
                    col.Item().Element(c => Section(c, "Detalle — Totales por categoría"));
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(1); c.RelativeColumn(1); });
                        Th(t, "Categoría"); Th(t, "Total"); Th(t, "%");
                        if (categories.Count == 0)
                        {
                            t.Cell().ColumnSpan(3).Border(0.5f).BorderColor(Hex(Hairline)).Padding(3)
                                .Text("Sin trámites en el periodo seleccionado.");
                        }
                        else
                        {
                            foreach (var c in categories)
                            {
                                Td(t, CategoryLabel(c.Category));
                                Td(t, c.Total.ToString(CultureInfo.InvariantCulture));
                                Td(t, Pct(c.Total, totalTramites));
                            }
                        }
                    });

                    col.Item().Element(c => Section(c, "Detalle — Top 5 radicadores"));
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        { c.RelativeColumn(3); c.RelativeColumn(1); c.RelativeColumn(1); c.RelativeColumn(1); });
                        Th(t, "Usuario"); Th(t, "Radicados"); Th(t, "Aprobados"); Th(t, "Rechazados");
                        if (data.TopProducers.Count == 0)
                        {
                            t.Cell().ColumnSpan(4).Border(0.5f).BorderColor(Hex(Hairline)).Padding(3)
                                .Text("Sin actividad de productividad en el periodo.");
                        }
                        else
                        {
                            foreach (var p in data.TopProducers)
                            {
                                Td(t, p.DisplayName);
                                Td(t, p.SubmittedCount.ToString(CultureInfo.InvariantCulture));
                                Td(t, p.ApprovedCount.ToString(CultureInfo.InvariantCulture));
                                Td(t, p.RejectedCount.ToString(CultureInfo.InvariantCulture));
                            }
                        }
                    });
                });

                page.Footer().AlignRight().Text(txt =>
                {
                    txt.Span("Generado por FLIT · ").FontSize(7).FontColor(Hex(Muted));
                    txt.Span($"{data.From:yyyy-MM-dd}/{data.To:yyyy-MM-dd}").FontSize(7).FontColor(Hex(Muted));
                });
            });
        }).GeneratePdf();
    }

    // ── Helpers de presentación ──────────────────────────────────────────────

    private static Color Hex(string hex) => Color.FromHex(hex);

    private static string Pct(int value, int total) =>
        total <= 0 ? "0%" : $"{Math.Round(value * 100.0 / total)}%";

    private static void Section(IContainer container, string title) =>
        container.PaddingTop(2).Text(txt => txt.Span(title).Bold().FontSize(11).FontColor(Hex(Ink)));

    private static void KpiCard(IContainer container, string label, int value, string accent) =>
        container.Border(0.5f).BorderColor(Hex(Hairline)).Padding(7).Column(col =>
        {
            col.Item().Text(label).FontSize(7).FontColor(Hex(Muted));
            col.Item().PaddingTop(2).Text(value.ToString(CultureInfo.InvariantCulture))
                .FontSize(15).Bold().FontColor(Hex(accent));
        });

    /// <summary>Fila etiqueta + barra horizontal proporcional (value/max) + valor a la derecha.</summary>
    private static void LabeledBar(IContainer container, string label, int value, int max, string colorHex, string trailing) =>
        container.Row(row =>
        {
            row.ConstantItem(110).AlignMiddle().Text(label).FontSize(8);
            row.RelativeItem().AlignMiddle().Height(11).Row(bar =>
            {
                var v = Math.Clamp(value, 0, Math.Max(max, 1));
                var rest = Math.Max(max, 1) - v;
                if (v > 0) bar.RelativeItem(v).Background(Hex(colorHex));
                if (rest > 0) bar.RelativeItem(rest).Background(Hex(Track));
            });
            row.ConstantItem(120).AlignMiddle().AlignRight().Text(trailing).FontSize(8).FontColor(Hex(Muted));
        });

    private static void Legend(IContainer container, string colorHex, string label) =>
        container.Row(row =>
        {
            row.AutoItem().AlignMiddle().Width(8).Height(8).Background(Hex(colorHex));
            row.AutoItem().PaddingLeft(3).AlignMiddle().Text(label).FontSize(7).FontColor(Hex(Muted));
        });

    private static void Th(TableDescriptor table, string label) =>
        table.Cell().Background(Hex(Track)).Border(0.5f).BorderColor(Hex(Hairline)).Padding(3)
            .Text(txt => txt.Span(label).FontSize(8).Bold());

    private static void Td(TableDescriptor table, string value) =>
        table.Cell().Border(0.5f).BorderColor(Hex(Hairline)).Padding(3)
            .Text(string.IsNullOrWhiteSpace(value) ? "-" : value).FontSize(8);

    // ── Etiquetas y colores (coherentes con el frontend) ─────────────────────

    private static string CategoryLabel(string category) => category switch
    {
        "matriculas" => "Matrículas",
        "traspasos" => "Traspasos",
        "vehicular" => "Vehicular",
        _ => "Otros",
    };

    private static string CategoryColor(string category) => category switch
    {
        "matriculas" => "#557EFF",
        "traspasos" => "#00DBD5",
        "vehicular" => "#9B8AFB",
        _ => "#F9AC00",
    };

    // Estados de negocio N 03 (ADR-0022). El fallback cubre datos previos a la migración.
    private static string StatusLabel(string status) => status switch
    {
        "borrador" => "Borrador",
        "preparado" => "Preparado",
        "entregado" => "Entregado",
        "aprobado" => "Aprobado",
        "rechazado" => "Rechazado",
        "anulado" => "Anulado",
        _ => status.Length == 0 ? "-" : char.ToUpperInvariant(status[0]) + status[1..].Replace('_', ' '),
    };

    private static string StatusColor(string status) => status switch
    {
        "borrador" => "#59677D",
        "preparado" => "#F9AC00",
        "entregado" => "#557EFF",
        "aprobado" => "#8CC63F",
        "rechazado" => "#FF4E00",
        "anulado" => "#E43D30",
        _ => "#9B8AFB",
    };
}
