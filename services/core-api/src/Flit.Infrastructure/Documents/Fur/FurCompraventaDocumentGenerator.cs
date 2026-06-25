using System.Globalization;
using Flit.Tramites.Application.Documents;
using QuestPDF;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Flit.Infrastructure.Documents.Fur;

/// <summary>Contrato de compraventa (traspaso) — QuestPDF simple, independiente del overlay FUR.</summary>
public static class FurCompraventaDocumentGenerator
{
    static FurCompraventaDocumentGenerator()
    {
        Settings.License = LicenseType.Community;
    }

    public static byte[] Generate(FurDocumentData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(9).FontFamily(Fonts.Arial));

                page.Content().Column(col =>
                {
                    col.Spacing(8);
                    col.Item().Text(txt => txt.Span("CONTRATO DE COMPRAVENTA").Bold().FontSize(13));
                    col.Item().Text($"Referencia: {data.ReferenceNumber}   |   Modalidad: {data.Modalidad}");

                    col.Item().PaddingTop(4).Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(90);
                            c.RelativeColumn();
                        });
                        Label(t, "Vehículo (placa)");
                        Value(t, data.Placa);
                        Label(t, "VIN");
                        Value(t, data.Vin);
                        Label(t, "Valor de venta");
                        Value(t, data.ValorVenta?.ToString("N2", CultureInfo.InvariantCulture));
                        Label(t, "Causal");
                        Value(t, data.Causal);
                    });

                    col.Item().PaddingTop(6).Text(txt => txt.Span("PARTES").Bold().FontSize(10));
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(1);
                            c.RelativeColumn(2);
                            c.RelativeColumn(2);
                        });
                        Header(t, "Rol");
                        Header(t, "Nombre");
                        Header(t, "Documento");
                        foreach (var p in data.Partes)
                        {
                            Value(t, p.Rol);
                            Value(t, p.Nombre);
                            Value(t, p.Documento);
                        }
                    });
                });
            });
        }).GeneratePdf();
    }

    private static void Label(TableDescriptor table, string label) =>
        table.Cell().Border(0.5f).Background(Colors.Grey.Lighten4).Padding(3)
            .Text(txt => txt.Span(label).FontSize(8).Bold().FontColor(Colors.Grey.Darken3));

    private static void Header(TableDescriptor table, string label) =>
        table.Cell().Background(Colors.Grey.Lighten3).Border(0.5f).Padding(3)
            .Text(txt => txt.Span(label).FontSize(8).Bold());

    private static void Value(TableDescriptor table, string? value) =>
        table.Cell().Border(0.5f).Padding(3).Text(Val(value));

    private static string Val(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value;
}
