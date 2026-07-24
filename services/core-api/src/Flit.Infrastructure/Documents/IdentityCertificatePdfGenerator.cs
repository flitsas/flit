using System.Globalization;
using Flit.Infrastructure.Documents.Branding;
using Flit.Tramites.Application.Documents;
using QuestPDF;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Certificado de validación de identidad como PDF real (QuestPDF) — reemplaza el mock text/plain
/// (HU #10458). Emite <c>application/pdf</c> (tipo 'certificado_identidad') para que pase
/// <c>IsMergeableMime</c> y se fusione como página del Expediente Consolidado. Mismo patrón
/// contract-first que <see cref="Fur.FurCompraventaDocumentGenerator"/>: sin tocar los handlers.
/// </summary>
public sealed class IdentityCertificatePdfGenerator : IIdentityCertificateGenerator
{
    static IdentityCertificatePdfGenerator()
    {
        Settings.License = LicenseType.Community;
    }

    public GeneratedDocument GenerateIdentityCertificate(IdentityCertificateData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var bytes = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                FlitLetterhead.ApplyTo(page); // HU #10856 — Carta + membrete FLIT + Poppins

                FlitLetterhead.Content(page).Column(col =>
                {
                    col.Spacing(10);
                    col.Item().Text(txt => txt.Span("CERTIFICADO DE VALIDACIÓN DE IDENTIDAD").Bold().FontSize(15));
                    col.Item().Text($"Referencia: {Val(data.ReferenceNumber)}   |   Instancia: {data.ProcedureInstanceId:D}")
                        .FontSize(8).FontColor(Colors.Grey.Darken2);

                    col.Item().PaddingTop(8).Text(txt => txt.Span("Comprador").Bold().FontSize(11));
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(120);
                            c.RelativeColumn();
                        });
                        Label(t, "Nombre");
                        Value(t, data.CompradorNombre);
                        Label(t, "Documento");
                        Value(t, data.CompradorDocumento);
                    });

                    col.Item().PaddingTop(6).Text(txt => txt.Span("Resultado de la validación biométrica").Bold().FontSize(11));
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(120);
                            c.RelativeColumn();
                        });
                        Label(t, "Score");
                        Value(t, data.Score.ToString(CultureInfo.InvariantCulture));
                        Label(t, "Resultado");
                        Value(t, data.Resultado);
                    });

                    col.Item().PaddingTop(10)
                        .Text($"Documento generado por FLIT el {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC.")
                        .FontSize(7).FontColor(Colors.Grey.Medium);
                });
            });
        }).GeneratePdf();

        var safeRef = Val(data.ReferenceNumber).Replace('/', '-');
        var filename = $"certificado_identidad_{safeRef}.pdf";
        return new GeneratedDocument("certificado_identidad", filename, "application/pdf", bytes);
    }

    private static void Label(TableDescriptor table, string label) =>
        table.Cell().Border(0.5f).Background(Colors.Grey.Lighten4).Padding(4)
            .Text(txt => txt.Span(label).FontSize(9).Bold().FontColor(Colors.Grey.Darken3));

    private static void Value(TableDescriptor table, string? value) =>
        table.Cell().Border(0.5f).Padding(4).Text(Val(value));

    // AC2: datos de identidad incompletos → marcador seguro ('-'); nunca vacío, nunca excepción.
    private static string Val(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}
