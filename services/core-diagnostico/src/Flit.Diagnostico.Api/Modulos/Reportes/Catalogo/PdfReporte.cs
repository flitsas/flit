using System.Globalization;
using Flit.Diagnostico.Api.Modulos.Reportes.Marca;
using Flit.Diagnostico.Domain.Consultas;
using QuestPDF.Elements;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Flit.Diagnostico.Api.Modulos.Reportes.Catalogo;

/// <summary>
/// PDF del reporte (HU #14579, BE-3; SP-15; contrato-api §5) con QuestPDF y la marca FLIT (<see cref="FlitLetterhead"/>,
/// <see cref="TablaFlit"/>): Legal horizontal, título, filtros y la grilla de la lista blanca; en el pie de <b>cada</b> página el aviso Ley
/// 1581, «Generado el …» y «Registro {acceso_id}». Metadatos <c>Author = "FLIT"</c> y <c>Creator = "FLIT Diagnóstico"</c>, con un título
/// sin compañía, usuario ni placa. Respeta el <see cref="CancellationToken"/> (CR-BE-5): antes de empezar, cada
/// <see cref="Bloque"/> filas al armar el texto, en cada página de la maquetación y del dibujo (<see cref="VigiaCancelacion"/>) y al
/// terminar; así una petición cancelada o el tiempo límite de <see cref="ExportadorReportes"/> liberan el hilo en una página.
/// </summary>
internal static class PdfReporte
{
    public const string ContentType = "application/pdf";

    /// <summary>Filas entre dos comprobaciones de cancelación al armar el texto de la grilla.</summary>
    public const int Bloque = 100;

    /// <summary>Licencia comunitaria de QuestPDF, como los demás PDF del servicio.</summary>
    static PdfReporte()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] Generar(ArchivoReporte archivo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(archivo);
        ct.ThrowIfCancellationRequested();
        FuentesFlit.Registrar();
        var generado = archivo.GeneradoEn.ToOffset(VigenciaDelDia.OffsetColombia);
        var registro = AvisoDatosPersonales.Registro(archivo.GeneradoEn, archivo.AccesoId);
        var titulo = $"Reporte de Diagnóstico: {archivo.NombreTipo}";
        var columnas = archivo.Columnas.Select(c => c.Titulo).ToList();
        var filas = new List<string[]>(archivo.Filas.Count);
        for (var i = 0; i < archivo.Filas.Count; i++)
        {
            if (i % Bloque == 0)
                ct.ThrowIfCancellationRequested();
            var fila = archivo.Filas[i];
            filas.Add([.. archivo.Columnas.Select(c => Texto(fila.GetValueOrDefault(c.Clave)))]);
        }

        var documento = Document.Create(documento => documento.Page(page =>
            {
                FlitLetterhead.ApplyTo(page);
                page.Size(PageSizes.Legal.Landscape());
                page.DefaultTextStyle(t => t.FontFamily(FlitDocumentTheme.FontRegular).FontSize(7).FontColor(FlitDocumentTheme.DarkNavy));
                page.Foreground().Dynamic(new VigiaCancelacion(ct));
                // El membrete es de una página Carta vertical: en Legal horizontal se ajusta al área (sin estirarse a lo ancho).
                page.Header().Height(FlitDocumentTheme.MarginCm, Unit.Centimetre).AlignTop().AlignCenter().Svg(BrandingAssets.MembreteHeaderSvg).FitArea();
                page.Footer().Column(pie =>
                {
                    pie.Item().PaddingHorizontal(FlitDocumentTheme.MarginCm, Unit.Centimetre).Text(AvisoDatosPersonales.Texto).FontSize(7).FontColor(FlitDocumentTheme.Gris);
                    pie.Item().PaddingHorizontal(FlitDocumentTheme.MarginCm, Unit.Centimetre).Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(7).FontColor(FlitDocumentTheme.Gris));
                        t.Span(registro + " · Página ");
                        t.CurrentPageNumber();
                        t.Span(" de ");
                        t.TotalPages();
                    });
                    pie.Item().Height(1.6f, Unit.Centimetre).AlignBottom().AlignCenter().Svg(BrandingAssets.MembreteFooterSvg).FitArea();
                });
                page.Content().PaddingHorizontal(FlitDocumentTheme.MarginCm, Unit.Centimetre).PaddingVertical(0.3f, Unit.Centimetre).Column(col =>
                {
                    col.Spacing(8);
                    col.Item().AlignCenter().Text(titulo).Bold().FontSize(13).FontColor(FlitDocumentTheme.PrimaryBlue);
                    col.Item().Element(c => TablaFlit.Pares(c, ExcelReporte.Filtros(archivo)));
                    col.Item().Element(c => TablaFlit.Grilla(c, columnas, filas, "Sin filas para los filtros elegidos."));
                });
            }))
            .WithMetadata(new DocumentMetadata
            {
                Title = titulo,
                Author = "FLIT",
                Creator = "FLIT Diagnóstico",
                Producer = "FLIT Diagnóstico",
                CreationDate = generado,
                ModifiedDate = generado,
            });
        byte[] bytes;
        try
        {
            bytes = documento.GeneratePdf();
        }
        catch (Exception ex) when (ct.IsCancellationRequested && ex is not OperationCanceledException)
        {
            // QuestPDF puede envolver la cancelación de la vigía en su propia excepción de maquetación.
            throw new OperationCanceledException("Generación del PDF cancelada.", ex, ct);
        }

        ct.ThrowIfCancellationRequested();
        return bytes;
    }

    /// <summary>
    /// Componente vacío en el primer plano de cada página: QuestPDF lo compone una vez por página en la maquetación y en el dibujo, y
    /// ahí se comprueba la cancelación. No dibuja nada.
    /// </summary>
    private sealed class VigiaCancelacion(CancellationToken ct) : IDynamicComponent
    {
        public DynamicComponentComposeResult Compose(DynamicContext context)
        {
            ct.ThrowIfCancellationRequested();
            return new DynamicComponentComposeResult { Content = context.CreateElement(_ => { }), HasMoreContent = false };
        }
    }

    private static string Texto(object? valor) => valor switch
    {
        null => string.Empty,
        string s => s,
        DateTimeOffset i => i.ToOffset(VigenciaDelDia.OffsetColombia).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        double d => d.ToString("0.####", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => valor.ToString() ?? string.Empty,
    };
}
