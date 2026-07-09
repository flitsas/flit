using Flit.Tramites.Application.Documents;
using QuestPDF;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Certificado RUES (Registro Único Empresarial y Social) como PDF real (QuestPDF) para la persona
/// jurídica del trámite (HU #10589, Feature #10583). Emite <c>application/pdf</c> (tipo
/// 'certificado_rues') para que pase <c>IsMergeableMime</c> y se fusione como página del Expediente
/// Consolidado. Mismo patrón contract-first que <see cref="IdentityCertificatePdfGenerator"/>: sin
/// tocar los handlers. En modo mock el estado llega como "ACTIVA".
/// </summary>
public sealed class RuesCertificatePdfGenerator : IRuesCertificateGenerator
{
    static RuesCertificatePdfGenerator()
    {
        Settings.License = LicenseType.Community;
    }

    public GeneratedDocument GenerateRuesCertificate(RuesCertificateData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var bytes = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(10).FontFamily(Fonts.Arial));

                page.Content().Column(col =>
                {
                    col.Spacing(10);
                    col.Item().Text(txt => txt.Span("CERTIFICADO RUES").Bold().FontSize(15));
                    col.Item().Text("Registro Único Empresarial y Social")
                        .FontSize(9).FontColor(Colors.Grey.Darken2);
                    col.Item().Text($"Referencia: {Val(data.ReferenceNumber)}   |   Instancia: {data.ProcedureInstanceId:D}")
                        .FontSize(8).FontColor(Colors.Grey.Darken2);

                    col.Item().PaddingTop(8).Text(txt => txt.Span("Persona jurídica").Bold().FontSize(11));
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(140);
                            c.RelativeColumn();
                        });
                        Label(t, "Razón social");
                        Value(t, data.RazonSocial);
                        Label(t, "NIT");
                        Value(t, data.Nit);
                        Label(t, "Estado en RUES");
                        Value(t, data.Estado);
                    });

                    col.Item().PaddingTop(10)
                        .Text($"Documento generado por FLIT el {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC.")
                        .FontSize(7).FontColor(Colors.Grey.Medium);
                });
            });
        }).GeneratePdf();

        var safeRef = Val(data.ReferenceNumber).Replace('/', '-');
        var filename = $"certificado_rues_{safeRef}.pdf";
        return new GeneratedDocument("certificado_rues", filename, "application/pdf", bytes);
    }

    private static void Label(TableDescriptor table, string label) =>
        table.Cell().Border(0.5f).Background(Colors.Grey.Lighten4).Padding(4)
            .Text(txt => txt.Span(label).FontSize(9).Bold().FontColor(Colors.Grey.Darken3));

    private static void Value(TableDescriptor table, string? value) =>
        table.Cell().Border(0.5f).Padding(4).Text(Val(value));

    // Datos incompletos → marcador seguro ('-'); nunca vacío, nunca excepción.
    private static string Val(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}
