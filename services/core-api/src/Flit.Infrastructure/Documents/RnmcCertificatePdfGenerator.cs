using Flit.Infrastructure.Documents.Branding;
using Flit.Tramites.Application.Documents;
using QuestPDF;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Certificado RNMC (Registro Nacional de Medidas Correctivas) como PDF real (QuestPDF) para las partes
/// del trámite (HU #10762, Feature #05). Emite <c>application/pdf</c> (tipo 'certificado_rnmc') para que
/// pase <c>IsMergeableMime</c> y se fusione como página del Expediente Consolidado. Mismo patrón
/// contract-first que <see cref="RuesCertificatePdfGenerator"/>: sin tocar los handlers.
/// <para>La fuente se pinta como la entidad oficial (Policía Nacional): el integrador que resuelve la
/// consulta NUNCA se expone al usuario.</para>
/// </summary>
public sealed class RnmcCertificatePdfGenerator : IRnmcCertificateGenerator
{
    private const string Fuente = "RNMC — Registro Nacional de Medidas Correctivas (Policía Nacional)";

    static RnmcCertificatePdfGenerator()
    {
        Settings.License = LicenseType.Community;
    }

    public GeneratedDocument GenerateRnmcCertificate(RnmcCertificateData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var entradas = data.Entradas ?? [];

        var bytes = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                FlitLetterhead.ApplyTo(page); // HU #10856 — Carta + membrete FLIT + Poppins

                FlitLetterhead.Content(page).Column(col =>
                {
                    col.Spacing(10);
                    col.Item().Text(txt => txt.Span("CERTIFICADO RNMC").Bold().FontSize(15));
                    col.Item().Text("Registro Nacional de Medidas Correctivas — Policía Nacional")
                        .FontSize(9).FontColor(Colors.Grey.Darken2);
                    col.Item().Text($"Referencia: {Val(data.ReferenceNumber)}   |   Instancia: {data.ProcedureInstanceId:D}")
                        .FontSize(8).FontColor(Colors.Grey.Darken2);
                    // HU #11049 — AÑO/MES/DÍA sin hora: sin hora, el sufijo UTC ya no aporta.
                    col.Item().Text($"Consultado el {FlitDocumentDate.Format(data.ConsultadoEn)}")
                        .FontSize(8).FontColor(Colors.Grey.Darken2);

                    // Sin consulta RNMC (p. ej. todas las partes son personas jurídicas): párrafo explícito
                    // en vez de una tabla vacía, que se leería como "consultado y sin resultados".
                    if (entradas.Count == 0)
                    {
                        col.Item().PaddingTop(8).Text("No se consultó el RNMC para este trámite.")
                            .FontSize(10).FontColor(Colors.Grey.Darken2);
                    }

                    foreach (var entrada in entradas)
                    {
                        col.Item().PaddingTop(8).Text(txt => txt.Span(Capitalize(entrada?.Rol)).Bold().FontSize(11));
                        col.Item().Table(t =>
                        {
                            t.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(140);
                                c.RelativeColumn();
                            });
                            Label(t, "Nombre");
                            Value(t, entrada?.Nombre);
                            Label(t, "Documento");
                            Value(t, entrada?.Documento);
                            Label(t, "Resultado");
                            Value(t, entrada?.Estado);
                            Label(t, "Detalle");
                            Value(t, entrada?.Detalle);
                        });
                    }

                    col.Item().PaddingTop(8).Text($"Fuente: {Fuente}")
                        .FontSize(8).FontColor(Colors.Grey.Darken2);

                    col.Item().PaddingTop(10)
                        .Text($"Documento generado por FLIT el {FlitDocumentDate.Format(DateTimeOffset.UtcNow)}.")
                        .FontSize(7).FontColor(Colors.Grey.Medium);
                });
            });
        }).GeneratePdf();

        var safeRef = Val(data.ReferenceNumber).Replace('/', '-');
        var filename = $"certificado_rnmc_{safeRef}.pdf";
        return new GeneratedDocument("certificado_rnmc", filename, "application/pdf", bytes);
    }

    private static void Label(TableDescriptor table, string label) =>
        table.Cell().Border(0.5f).Background(Colors.Grey.Lighten4).Padding(4)
            .Text(txt => txt.Span(label).FontSize(9).Bold().FontColor(Colors.Grey.Darken3));

    private static void Value(TableDescriptor table, string? value) =>
        table.Cell().Border(0.5f).Padding(4).Text(Val(value));

    // Datos incompletos → marcador seguro ('-'); nunca vacío, nunca excepción.
    private static string Val(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private static string Capitalize(string? rol)
    {
        var v = Val(rol);
        return v.Length == 0 ? v : char.ToUpperInvariant(v[0]) + v[1..];
    }
}
