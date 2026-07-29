using Flit.Infrastructure.Documents.Branding;
using Flit.Tramites.Application.Documents;
using QuestPDF;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// "Certificado de vigencia SOAT Y RTM" como PDF real (QuestPDF) con membrete FLIT (HU #10856, punto 6),
/// con el estilo de la muestra oficial (recursos dllo membrete/hojas flit/ejemplo.pdf): celdas tipo
/// "chip" (cabecera azul de marca con texto blanco + valor en celda clara), en grilla de 3 columnas por
/// ramo (SOAT/RTM) y, solo en traspaso, una tabla de Avalúo con una fila por fuente. Los datos vienen de
/// la consulta al RUNT (<c>field_values</c>) y del avalúo (Feature #10707); valores ausentes → EN BLANCO.
/// <para>Nota: QuestPDF no soporta corner-radius nativo; las esquinas redondeadas (~20px) se dibujan con
/// una capa SVG de rect redondeado detrás de cada celda (<see cref="FlitRoundedCells"/>), la misma técnica de
/// SVG que usa el membrete. Gradientes tampoco: se usa azul de marca sólido (#557EFF).</para>
/// </summary>
public sealed class SoatRtmCertificatePdfGenerator : ISoatRtmCertificateGenerator
{
    private const string HeaderBg = FlitRoundedCells.HeaderBg; // #557EFF
    private const string ValueBg = FlitRoundedCells.ValueBg;
    private const string White = FlitRoundedCells.White;

    /// <summary>HU #10856 — separación de filas (tr) y celdas (td) de las tablas: 0,1 cm (apenas perceptible).</summary>
    private const float CellGapCm = 0.1f;

    // Alturas de viewBox: celda estándar y cabecera de avalúo (más ancha ⇒ viewBox más bajo para igualar alto).
    private const int VbCell = FlitRoundedCells.VbCell;
    private const int VbAvaluoHeader = 12;

    static SoatRtmCertificatePdfGenerator()
    {
        Settings.License = LicenseType.Community;
    }

    public GeneratedDocument GenerateSoatRtmCertificate(SoatRtmCertificateData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var bytes = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                FlitLetterhead.ApplyTo(page); // Carta + membrete FLIT + Poppins

                // HU #10856 — margen del documento FLIT: 2,54 cm en los cuatro lados. Las bandas de
                // membrete ya ocupan 2,54 cm arriba/abajo, así que el contenido va sin padding vertical
                // extra (flush con las bandas) y con 2,54 cm laterales. Encabezado/pie se mantienen igual.
                FlitLetterhead.Content(page, horizontalCm: FlitDocumentTheme.MarginCm, verticalCm: 0f).Column(col =>
                {
                    col.Spacing(8);
                    col.Item().Text("FLIT ECOSISTEMA DE TRÁNSITO Y TRANSPORTE CERTIFICA QUE:")
                        .Bold().FontSize(11).FontColor(FlitDocumentTheme.DarkNavy);
                    col.Item().Text(Intro(data))
                        .FontSize(9).FontColor(FlitDocumentTheme.DarkNavy);

                    Ramo(col, "SOAT", "Póliza", "Entidad expide SOAT", data.Soat);
                    if (data.Rtm is not null) // matrícula inicial: sin RTM.
                        Ramo(col, "RTM", "RTM", "Entidad expide RTM", data.Rtm);

                    if (data.Avaluo is not null)
                        Avaluo(col, data.Avaluo);
                });
            });
        }).GeneratePdf();

        var safeRef = Ref(data.ReferenceNumber);
        return new GeneratedDocument("certificado_soat_rtm", $"certificado_soat_rtm_{safeRef}.pdf", "application/pdf", bytes);
    }

    private static string Intro(SoatRtmCertificateData data)
    {
        var placa = Disp(data.Placa);
        var fecha = Disp(data.FechaConsulta);
        var cuando = string.IsNullOrEmpty(fecha) ? "" : $" el día {fecha}";
        var ramos = data.Rtm is not null ? "SOAT y REVISIÓN TECNOMECÁNICA" : "SOAT";
        return $"En la consulta realizada al RUNT 2.0{cuando} el vehículo de placas {placa} "
            + $"se encontró la siguiente información del estado de {ramos}.";
    }

    // Un ramo (SOAT/RTM): título centrado + grilla de 6 campos en dos filas de 3 chips.
    private static void Ramo(ColumnDescriptor col, string titulo, string numeroLabel, string entidadLabel, SoatRtmBlock b)
    {
        col.Item().PaddingTop(4).AlignCenter().Text($"Datos {titulo}")
            .Bold().FontSize(11).FontColor(FlitDocumentTheme.DarkNavy);

        // Grilla del ramo (2 filas × 3 chips): separación de filas (tr) y celdas (td) = 0,1 cm.
        col.Item().Column(tabla =>
        {
            tabla.Spacing(CellGapCm, Unit.Centimetre);
            // Solo se redondean las esquinas EXTERNAS de la fila (primera celda a la izquierda, última a
            // la derecha); las uniones internas van rectas para que la fila se lea como una barra continua.
            tabla.Item().Row(row =>
            {
                row.Spacing(CellGapCm, Unit.Centimetre);
                Chip(row.RelativeItem(), $"N° {numeroLabel}", b.Poliza, roundLeft: true, roundRight: false);
                Chip(row.RelativeItem(), "Fecha expedición", b.FechaExpedicion, roundLeft: false, roundRight: false);
                Chip(row.RelativeItem(), "Fecha vigencia", b.FechaVigencia, roundLeft: false, roundRight: true);
            });
            tabla.Item().Row(row =>
            {
                row.Spacing(CellGapCm, Unit.Centimetre);
                Chip(row.RelativeItem(), "Fecha de vencimiento", b.FechaVencimiento, roundLeft: true, roundRight: false);
                Chip(row.RelativeItem(), "Estado", b.Estado, roundLeft: false, roundRight: false);
                Chip(row.RelativeItem(), entidadLabel, b.Entidad, roundLeft: false, roundRight: true);
            });
        });
    }

    // Chip tipo tarjeta: cabecera azul arriba + valor claro abajo. Solo se redondean las esquinas del
    // lado externo de la fila (roundLeft = primera celda, roundRight = última): la cabecera redondea las
    // superiores y el valor las inferiores de ese lado; las uniones internas quedan rectas.
    private static void Chip(IContainer container, string label, string? value, bool roundLeft, bool roundRight)
    {
        container.Column(c =>
        {
            FlitRoundedCells.Cell(c.Item(), HeaderBg, tl: roundLeft, tr: roundRight, br: false, bl: false, VbCell, inner =>
                inner.PaddingHorizontal(6).AlignMiddle()
                    .Text(label).Bold().FontSize(8).FontColor(White));
            FlitRoundedCells.Cell(c.Item(), ValueBg, tl: false, tr: false, br: roundRight, bl: roundLeft, VbCell, inner =>
                inner.PaddingHorizontal(6).AlignMiddle()
                    .Text(Disp(value)).FontSize(8).FontColor(FlitDocumentTheme.DarkNavy));
        });
    }

    // Avalúo (solo traspaso): tabla de 2 columnas con cabecera azul y una fila por fuente.
    private static void Avaluo(ColumnDescriptor col, AvaluoInfo a)
    {
        col.Item().PaddingTop(6).AlignCenter().Text("Avalúo del vehículo")
            .Bold().FontSize(11).FontColor(FlitDocumentTheme.DarkNavy);

        // Tabla de avalúo (cabecera + una fila por fuente): tr y td separados 0,1 cm.
        col.Item().Column(tabla =>
        {
            tabla.Spacing(CellGapCm, Unit.Centimetre);
            // Cabecera: unión interna recta (solo esquinas externas redondeadas). Su viewBox usa un alto
            // menor (VbAvaluoHeader) para que, siendo celdas más anchas que las de SOAT/RTM, resulten con
            // el MISMO alto que aquellas cabeceras.
            tabla.Item().Row(row =>
            {
                row.Spacing(CellGapCm, Unit.Centimetre);
                HeaderCell(row.RelativeItem(), "Tipo de avalúo", roundLeft: true, roundRight: false);
                HeaderCell(row.RelativeItem(), "Valor avalúo", roundLeft: false, roundRight: true);
            });
            foreach (var r in a.Rows)
            {
                tabla.Item().Row(row =>
                {
                    row.Spacing(CellGapCm, Unit.Centimetre);
                    ValueCell(row.RelativeItem(), r.Tipo);
                    ValueCell(row.RelativeItem(), r.Valor);
                });
            }
        });
    }

    // Cabecera de avalúo: redondea solo la esquina SUPERIOR externa (unión central recta y borde inferior
    // recto para conectar con las filas de valores). Usa VbAvaluoHeader para igualar el alto de las
    // cabeceras SOAT/RTM pese a ser celdas más anchas.
    private static void HeaderCell(IContainer container, string text, bool roundLeft, bool roundRight) =>
        FlitRoundedCells.Cell(container, HeaderBg, tl: roundLeft, tr: roundRight, br: false, bl: false, VbAvaluoHeader, inner =>
            inner.PaddingHorizontal(6).AlignMiddle()
                .Text(text).Bold().FontSize(8).FontColor(White));

    private static void ValueCell(IContainer container, string? value) =>
        FlitRoundedCells.Cell(container, ValueBg, tl: true, tr: true, br: true, bl: true, VbCell, inner =>
            inner.PaddingHorizontal(6).AlignMiddle()
                .Text(Disp(value)).FontSize(8).FontColor(FlitDocumentTheme.DarkNavy));

    // Regla de negocio (HU #10856): valor ausente en la consulta → EN BLANCO (no marcador).
    private static string Disp(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    // Solo para el nombre de archivo: fallback no vacío para no romper la ruta de storage.
    private static string Ref(string? reference) =>
        string.IsNullOrWhiteSpace(reference) ? "sin-referencia" : reference.Trim().Replace('/', '-');
}
