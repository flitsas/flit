using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Flit.Impuestos.Api.Modulos.Reportes;

/// <summary>
/// PDF del reporte (plan §5.1, M14) con QuestPDF, en la forma del PDF de reportes de Diagnóstico (sin referenciarlo, EP-CF1): Legal
/// horizontal, el nombre del reporte (<c>x-flit-etiquetas</c> de <c>TipoReporte</c>) y el periodo, la grilla con las etiquetas de las
/// columnas repetidas en cada página y el número de página al pie. Sin truncar: el tope de 1000 filas ya se comprobó. Metadatos sin
/// compañía, usuario ni placa. Respeta la cancelación antes de empezar y cada <see cref="Bloque"/> filas al armar la grilla.
/// </summary>
/// <example>
/// <code>
/// var bytes = PdfReporte.Generar(ArchivoReporte.Armar(definicion, rango, columnas, filas), ct);
/// </code>
/// </example>
internal static class PdfReporte
{
    public const string ContentType = "application/pdf";
    public const int Bloque = 100;

    private const string Azul = "#1B3A6B";
    private const string Gris = "#6B7280";
    private const string Borde = "#D1D5DB";
    private const string Encabezado = "#EEF2F7";

    static PdfReporte()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = false;
    }

    public static byte[] Generar(ArchivoReporte archivo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(archivo);
        ct.ThrowIfCancellationRequested();
        var titulo = archivo.Definicion.Nombre;
        var periodo = string.Create(CultureInfo.InvariantCulture, $"{archivo.Rango.Desde:yyyy-MM-dd} a {archivo.Rango.Hasta:yyyy-MM-dd}");
        var documento = Document.Create(d => d.Page(page =>
            {
                page.Size(PageSizes.Legal.Landscape());
                page.Margin(1.2f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(7).FontColor(Azul));
                page.Header().Column(c =>
                {
                    c.Item().Text(titulo).Bold().FontSize(13);
                    c.Item().Text(periodo).FontSize(8).FontColor(Gris);
                });
                page.Content().PaddingVertical(0.3f, Unit.Centimetre).Table(tabla =>
                {
                    tabla.ColumnsDefinition(c =>
                    {
                        foreach (var _ in archivo.Columnas)
                            c.RelativeColumn();
                    });
                    tabla.Header(h =>
                    {
                        foreach (var columna in archivo.Columnas)
                            h.Cell().Background(Encabezado).Border(0.5f).BorderColor(Borde).Padding(3).Text(columna.Etiqueta).Bold();
                    });
                    for (var i = 0; i < archivo.Filas.Count; i++)
                    {
                        if (i % Bloque == 0)
                            ct.ThrowIfCancellationRequested();
                        foreach (var celda in archivo.Filas[i])
                            tabla.Cell().Border(0.5f).BorderColor(Borde).Padding(3).Text(celda.Texto);
                    }
                });
                page.Footer().AlignRight().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(7).FontColor(Gris));
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
            }))
            .WithMetadata(new DocumentMetadata { Title = titulo, Author = "FLIT", Creator = "FLIT Impuestos", Producer = "FLIT Impuestos" });
        var bytes = documento.GeneratePdf();
        ct.ThrowIfCancellationRequested();
        return bytes;
    }
}
