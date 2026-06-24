using Flit.Tramites.Application.Documents;
using QuestPDF;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Generador PDF real del Formulario Único Nacional (FUN) para matrícula inicial y del
/// contrato de compraventa para traspaso. Usa QuestPDF Community (revenue &lt; USD 1 M/año).
/// Reemplaza a <see cref="MockFurDocumentGenerator"/> que emitía texto plano (HU #10256).
/// </summary>
public sealed class FurDocumentGenerator : IFurDocumentGenerator
{
    static FurDocumentGenerator()
    {
        Settings.License = LicenseType.Community;
    }

    public GeneratedDocument GenerateFur(FurDocumentData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var bytes = BuildDocument(data, "FORMULARIO ÚNICO NACIONAL — MATRÍCULA INICIAL", includeComercial: false);
        return new GeneratedDocument("fur", $"fur_{SafeRef(data.ReferenceNumber)}.pdf", "application/pdf", bytes);
    }

    public GeneratedDocument GenerateCompraventa(FurDocumentData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var bytes = BuildDocument(data, "CONTRATO DE COMPRAVENTA", includeComercial: true);
        return new GeneratedDocument("compraventa", $"compraventa_{SafeRef(data.ReferenceNumber)}.pdf", "application/pdf", bytes);
    }

    // ── Document builder ────────────────────────────────────────────────────

    private static byte[] BuildDocument(FurDocumentData data, string titulo, bool includeComercial) =>
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(9));

                page.Content().Column(col =>
                {
                    col.Spacing(8);
                    RenderHeader(col, data, titulo);
                    RenderVehiculoSection(col, data.Vehiculo);
                    RenderPartesSection(col, data.Partes);
                    if (includeComercial)
                        RenderComercialSection(col, data);
                    RenderFirmasSection(col, data);
                });
            });
        }).GeneratePdf();

    // ── Sections ────────────────────────────────────────────────────────────

    private static void RenderHeader(ColumnDescriptor col, FurDocumentData data, string titulo)
    {
        col.Item().BorderBottom(1).PaddingBottom(6).Column(hdr =>
        {
            hdr.Item().DefaultTextStyle(t => t.Bold().FontSize(13)).Text(titulo);
            hdr.Item().PaddingTop(2).Text(
                $"Referencia: {data.ReferenceNumber}  |  Modalidad: {data.Modalidad}");
            hdr.Item().DefaultTextStyle(t => t.FontSize(8)).Text(
                $"Organismo de Tránsito: {data.Organismo.Nombre ?? "-"}  " +
                $"({data.Organismo.Codigo ?? "-"})  —  {data.Organismo.Ciudad ?? "-"}");
        });
    }

    private static void RenderVehiculoSection(ColumnDescriptor col, VehiculoDatos v)
    {
        col.Item().DefaultTextStyle(t => t.Bold().FontSize(10)).Text("DATOS DEL VEHÍCULO");

        col.Item().Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.ConstantColumn(85);
                cols.RelativeColumn(1);
                cols.ConstantColumn(85);
                cols.RelativeColumn(1);
            });

            // 9 base + 8 nuevos HU #10256 = 17 campos → 9 filas (última con celda vacía)
            var fields = new (string Label, string? Value)[]
            {
                ("Marca",              v.Marca),
                ("Línea",              v.Linea),
                ("Modelo (Año)",       v.Modelo),
                ("Color",              v.Color),
                ("Clase",              v.Clase),
                ("Combustible",        v.Combustible),
                ("Cilindraje (cc)",    v.Cilindraje),
                ("VIN",                v.Vin),
                ("Placa",              v.Placa),
                ("No. Motor",          v.NumeroMotor),
                ("No. Chasis",         v.NumeroChasis),
                ("No. Serie",          v.NumeroSerie),
                ("Tipo Carrocería",    v.TipoCarroceria),
                ("Tipo Servicio",      v.TipoServicio),
                ("Capacidad (Pas.)",   v.Capacidad),
                ("Peso Bruto (kg)",    v.PesoBruto),
                ("No. Ejes",           v.NumeroEjes),
            };

            for (var i = 0; i < fields.Length; i += 2)
            {
                LabelCell(table, fields[i].Label);
                ValueCell(table, fields[i].Value);

                if (i + 1 < fields.Length)
                {
                    LabelCell(table, fields[i + 1].Label);
                    ValueCell(table, fields[i + 1].Value);
                }
                else
                {
                    // padding cell para completar la fila
                    table.Cell().Border(0.5f);
                    table.Cell().Border(0.5f);
                }
            }
        });
    }

    private static void RenderPartesSection(ColumnDescriptor col, IReadOnlyList<DocumentParte> partes)
    {
        col.Item().DefaultTextStyle(t => t.Bold().FontSize(10)).Text("PARTES DEL TRÁMITE");

        col.Item().Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(1);
                cols.RelativeColumn(2);
                cols.RelativeColumn(2);
                cols.RelativeColumn(3);
            });

            HeaderCell(table, "Rol");
            HeaderCell(table, "Nombre completo");
            HeaderCell(table, "No. Documento");
            HeaderCell(table, "Correo electrónico");

            foreach (var p in partes)
            {
                ValueCell(table, p.Rol);
                ValueCell(table, p.Nombre);
                ValueCell(table, p.Documento);
                ValueCell(table, p.Email);
            }
        });
    }

    private static void RenderComercialSection(ColumnDescriptor col, FurDocumentData data)
    {
        col.Item().DefaultTextStyle(t => t.Bold().FontSize(10)).Text("INFORMACIÓN COMERCIAL");

        col.Item().Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.ConstantColumn(85);
                cols.RelativeColumn(1);
                cols.ConstantColumn(85);
                cols.RelativeColumn(1);
            });

            LabelCell(table, "Valor de Venta");
            ValueCell(table, data.ValorVenta?.ToString("N2") ?? "-");
            LabelCell(table, "Causal");
            ValueCell(table, data.Causal);
        });
    }

    private static void RenderFirmasSection(ColumnDescriptor col, FurDocumentData data)
    {
        col.Item().DefaultTextStyle(t => t.Bold().FontSize(10)).Text("SELLOS DE FIRMA ELECTRÓNICA");

        if (data.SellosFirma.Count == 0)
        {
            col.Item().DefaultTextStyle(t => t.Italic()).Text("(sin sellos registrados)");
        }
        else
        {
            foreach (var sello in data.SellosFirma)
                col.Item().DefaultTextStyle(t => t.FontSize(8)).Text($"• {sello}");
        }

        col.Item().PaddingTop(16).Row(row =>
        {
            foreach (var parte in data.Partes)
            {
                row.RelativeItem().BorderTop(0.5f).PaddingTop(4).PaddingRight(8).Column(c =>
                {
                    c.Item().Height(28);
                    c.Item().DefaultTextStyle(t => t.FontSize(8)).Text(parte.Nombre ?? "-");
                    c.Item().DefaultTextStyle(t => t.FontSize(7).FontColor(Colors.Grey.Darken2)).Text($"C.C. {parte.Documento ?? "-"}");
                    c.Item().DefaultTextStyle(t => t.FontSize(7).Bold()).Text(parte.Rol.ToUpperInvariant());
                });
            }

            row.RelativeItem().BorderTop(0.5f).PaddingTop(4).PaddingLeft(8).Column(c =>
            {
                c.Item().Height(28);
                c.Item().DefaultTextStyle(t => t.FontSize(7).Italic().FontColor(Colors.Grey.Darken2))
                    .Text("Sello y firma del Organismo de Tránsito");
                c.Item().DefaultTextStyle(t => t.FontSize(8)).Text(data.Organismo.Nombre ?? "-");
            });
        });
    }

    // ── Cell helpers ────────────────────────────────────────────────────────

    private static void LabelCell(TableDescriptor table, string label) =>
        table.Cell().Border(0.5f).Background(Colors.Grey.Lighten4).Padding(3)
             .DefaultTextStyle(t => t.FontSize(7).Bold().FontColor(Colors.Grey.Darken3))
             .Text(label);

    private static void HeaderCell(TableDescriptor table, string label) =>
        table.Cell().Background(Colors.Grey.Lighten3).Border(0.5f).Padding(3)
             .DefaultTextStyle(t => t.FontSize(8).Bold())
             .Text(label);

    private static void ValueCell(TableDescriptor table, string? value) =>
        table.Cell().Border(0.5f).Padding(3).Text(value ?? "-");

    private static string SafeRef(string reference) =>
        reference.Replace('/', '-').Replace('\\', '-');
}
