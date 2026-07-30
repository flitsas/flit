using Flit.Infrastructure.Documents.Branding;
using Flit.Tramites.Application.Documents;
using QuestPDF;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Certificado RUES (Registro Único Empresarial y Social) como PDF real (QuestPDF) con membrete FLIT
/// para la persona jurídica del trámite (HU #10589, Feature #10583/#10852). Sigue la muestra oficial
/// (recursos dllo membrete/PDF/RUES.pdf): tres secciones — REGISTRO COMERCIAL (grilla de chips FLIT),
/// REPRESENTACIÓN LEGAL (bloque de texto) y ACTIVIDADES ECONÓMICAS (tabla). Emite <c>application/pdf</c>
/// (tipo 'certificado_rues') para fusionarse en el Expediente Consolidado. Valores ausentes → EN BLANCO.
/// </summary>
public sealed class RuesCertificatePdfGenerator : IRuesCertificateGenerator
{
    private const string HeaderBg = FlitRoundedCells.HeaderBg;
    private const string ValueBg = FlitRoundedCells.ValueBg;
    private const string White = FlitRoundedCells.White;

    // viewBox por ancho de celda (el .Svg toma alto proporcional): barra a ancho completo vs chip de 3 col.
    private const int VbBar = 5;   // barra de sección (celda a ancho completo) → ~23 pt de alto
    private const int VbCell = FlitRoundedCells.VbCell; // chip de la grilla de 3 columnas → ~28 pt

    private const float GapCm = 0.1f; // separación tr/td (igual que el certificado SOAT/RTM)

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
                FlitLetterhead.ApplyTo(page); // Carta + membrete FLIT + Poppins

                FlitLetterhead.Content(page, horizontalCm: FlitDocumentTheme.MarginCm, verticalCm: 0f).Column(col =>
                {
                    col.Spacing(8);

                    col.Item().Text($"Certificación Consulta RUES: NIT - {Disp(data.Nit)}")
                        .Bold().FontSize(12).FontColor(FlitDocumentTheme.DarkNavy);
                    col.Item().Text(
                        $"En la consulta realizada al RUES sobre el NIT {Disp(data.Nit)} se encontró la siguiente información.")
                        .FontSize(9).FontColor(FlitDocumentTheme.DarkNavy);

                    // ── REGISTRO COMERCIAL ──────────────────────────────────────────────────────────
                    SectionBar(col, "REGISTRO COMERCIAL");
                    RegistroComercialGrid(col, data);

                    // ── REPRESENTACIÓN LEGAL ────────────────────────────────────────────────────────
                    SectionBar(col, "REPRESENTACIÓN LEGAL");
                    col.Item().Background(ValueBg).PaddingVertical(6).PaddingHorizontal(8)
                        .Text(Disp(data.RepresentacionLegal))
                        .FontSize(7.5f).FontColor(FlitDocumentTheme.DarkNavy).Justify();

                    // ── ACTIVIDADES ECONÓMICAS ──────────────────────────────────────────────────────
                    SectionBar(col, "ACTIVIDADES ECONÓMICAS");
                    ActividadesTable(col, data.Actividades);
                });
            });
        }).GeneratePdf();

        var safeRef = Val(data.ReferenceNumber).Replace('/', '-');
        return new GeneratedDocument("certificado_rues", $"certificado_rues_{safeRef}.pdf", "application/pdf", bytes);
    }

    // Barra de sección a ancho completo: celda azul con las 4 esquinas redondeadas y título blanco centrado.
    private static void SectionBar(ColumnDescriptor col, string title) =>
        col.Item().PaddingTop(4).Element(e =>
            FlitRoundedCells.Cell(e, HeaderBg, tl: true, tr: true, br: true, bl: true, VbBar, inner =>
                inner.AlignMiddle().AlignCenter()
                    .Text(title).Bold().FontSize(10).FontColor(White)));

    // Grilla del REGISTRO COMERCIAL: pares llave/valor como chips verticales (etiqueta arriba + valor
    // abajo), 3 por fila; solo se redondean las esquinas externas de cada fila (uniones internas rectas).
    private static void RegistroComercialGrid(ColumnDescriptor col, RuesCertificateData d)
    {
        (string Label, string? Value)[] campos =
        [
            ("NIT", d.Nit),
            ("Acrónimo", d.Sigla),
            ("Nombre Negocio", d.RazonSocial),
            ("Cámara Comercio", d.CamaraComercio),
            ("Dirección Comercial", d.Direccion),
            ("Ubicación", d.Municipio),
            ("Tipo Compañía", d.TipoCompania),
            ("Email", d.Email),
            ("Categoría Inscripción", d.Categoria),
            // HU #11049 — AÑO/MES/DÍA sin hora. Las fechas llegan como texto del RUES: si no se pueden
            // interpretar se imprimen tal cual (nunca se inventa ni se vacía un dato del certificado).
            ("Fecha Inscripción", FlitDocumentDate.Normalize(d.FechaMatricula)),
            ("Id", d.IdRm),
            ("Año Renovado", d.UltimoAnoRenovado),
            ("Fecha Renovación", FlitDocumentDate.Normalize(d.FechaRenovacion)),
            ("Fecha Actualización", FlitDocumentDate.Normalize(d.FechaActualizacion)),
            ("Tipo Organización", d.TipoOrganizacion),
            ("Razón Cancelación", d.RazonCancelacion),
            ("Número Registro", d.MatriculaMercantil),
            ("Estado Registro", d.Estado),
        ];

        col.Item().Column(grid =>
        {
            grid.Spacing(GapCm, Unit.Centimetre);
            for (var i = 0; i < campos.Length; i += 3)
            {
                grid.Item().Row(row =>
                {
                    row.Spacing(GapCm, Unit.Centimetre);
                    for (var j = 0; j < 3 && i + j < campos.Length; j++)
                    {
                        var (label, value) = campos[i + j];
                        Field(row.RelativeItem(), label, value, roundLeft: j == 0, roundRight: j == 2 || i + j == campos.Length - 1);
                    }
                });
            }
        });
    }

    // Chip vertical: etiqueta (azul, esquinas superiores externas) + valor (claro, inferiores externas).
    private static void Field(IContainer container, string label, string? value, bool roundLeft, bool roundRight) =>
        container.Column(c =>
        {
            FlitRoundedCells.Cell(c.Item(), HeaderBg, tl: roundLeft, tr: roundRight, br: false, bl: false, VbCell, inner =>
                inner.PaddingHorizontal(6).AlignMiddle()
                    .Text(label).Bold().FontSize(7.5f).FontColor(White));
            FlitRoundedCells.Cell(c.Item(), ValueBg, tl: false, tr: false, br: roundRight, bl: roundLeft, VbCell, inner =>
                inner.PaddingHorizontal(6).AlignMiddle()
                    .Text(Disp(value)).FontSize(7.5f).FontColor(FlitDocumentTheme.DarkNavy));
        });

    // ACTIVIDADES ECONÓMICAS: tabla nativa (cabecera azul + filas claras). La descripción es de alto
    // variable, así que aquí se usa tabla con bordes blancos en vez de chips de alto fijo.
    private static void ActividadesTable(ColumnDescriptor col, IReadOnlyList<RuesActividad>? actividades)
    {
        col.Item().Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                c.ConstantColumn(55);
                c.RelativeColumn(2);
                c.RelativeColumn(4);
            });

            HeadCell(t, "Código");
            HeadCell(t, "Nombre");
            HeadCell(t, "Descripción");

            if (actividades is not null)
            {
                foreach (var a in actividades)
                {
                    BodyCell(t, a.Codigo);
                    BodyCell(t, a.Nombre);
                    BodyCell(t, a.Descripcion);
                }
            }
        });
    }

    private static void HeadCell(TableDescriptor t, string text) =>
        t.Cell().Background(HeaderBg).Border(1).BorderColor(White).PaddingVertical(4).PaddingHorizontal(6)
            .Text(text).Bold().FontSize(8).FontColor(White);

    private static void BodyCell(TableDescriptor t, string? value) =>
        t.Cell().Background(ValueBg).Border(1).BorderColor(White).PaddingVertical(4).PaddingHorizontal(6)
            .Text(Disp(value)).FontSize(7.5f).FontColor(FlitDocumentTheme.DarkNavy);

    // Contenido: dato ausente en la consulta → EN BLANCO (regla del negocio HU #10856/#10589).
    private static string Disp(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    // Solo para el nombre de archivo: fallback no vacío para no romper la ruta de storage.
    private static string Val(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}
