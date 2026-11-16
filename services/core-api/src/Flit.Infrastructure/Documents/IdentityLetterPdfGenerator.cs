using Flit.Infrastructure.Documents.Branding;
using Flit.Queries.Domain.Time;
using Flit.Tramites.Application.Identity;
using QuestPDF;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// HU #14301 (Feature #14300, Épica #14271) — PDF de la carta de identidad que FLIT emite al aprobar una validación MANUAL.
/// Una hoja Carta con la disposición del certificado de verificación (barra, logo, título, solicitud, banda del rol, datos,
/// código de integridad, trazabilidad, evidencia y firma) con la marca y los colores de FLIT (<c>globals.css</c> →
/// <c>@flit/ui/tokens.css</c>: brand #557EFF, primary #162744, bg #EEF5FF, success #4F7A12).
/// <para>NO imprime QR, score, proveedor externo, fecha de nacimiento, sexo ni el nombre del usuario que revisó; el pie es
/// solo «FLIT», sin validez legal citada. Fechas en hora de Colombia con el formato del producto
/// (<see cref="FlitDocumentDate"/>), sin depender de la cultura del servidor.</para>
/// </summary>
public sealed class IdentityLetterPdfGenerator : IIdentityLetterPdfGenerator
{
    // Tokens de @flit/ui (fuente de verdad: frontend/app/globals.css).
    private const string Brand = "#557EFF";
    private const string Navy = "#162744";
    private const string BrandBg = "#EEF5FF";
    private const string SuccessInk = "#4F7A12";
    private const string Muted = "#6B7280";
    private const string EvidenceBorder = "#DFE5ED";
    private const string Mono = "Courier New";

    static IdentityLetterPdfGenerator()
    {
        Settings.License = LicenseType.Community;
    }

    public byte[] Generate(IdentityLetterData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        FlitFonts.EnsureRegistered();

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(28);
                page.DefaultTextStyle(t => t.FontFamily(FlitDocumentTheme.FontRegular).FontSize(8).FontColor(Navy));

                page.Content().Column(col =>
                {
                    col.Spacing(5);

                    col.Item().Height(4).Background(Brand);
                    col.Item().Element(c => Header(c, data));
                    col.Item().Element(c => Solicitud(c, data));
                    col.Item().Element(c => RoleBand(c, data));
                    col.Item().Element(c => Datos(c, data));
                    col.Item().Element(c => Section(c, "CÓDIGO DE INTEGRIDAD"));
                    col.Item().Element(c => Integridad(c, data));
                    col.Item().Element(c => Section(c, "TRAZABILIDAD Y CONSENTIMIENTO"));
                    col.Item().Element(c => Trazabilidad(c, data));
                    col.Item().Element(c => Section(c, "EVIDENCIA CAPTURADA"));
                    col.Item().Element(c => Evidencia(c, data));
                    col.Item().Element(c => Section(c, "FIRMA Y AUTORIZACIÓN"));
                    col.Item().Element(c => Firma(c, data));
                });

                page.Footer().AlignCenter().Text("FLIT").FontSize(8).Bold().FontColor(Brand);
            });
        }).GeneratePdf();
    }

    private static void Header(IContainer c, IdentityLetterData d) =>
        c.PaddingVertical(2).Row(row =>
        {
            row.ConstantItem(86).AlignMiddle().Height(32).Svg(BrandingAssets.Svg("logo-flit-dark.svg"));
            row.RelativeItem().AlignCenter().Column(col =>
            {
                col.Item().AlignCenter().Text("CERTIFICACIÓN DE VALIDACIÓN DE IDENTIDAD")
                    .FontSize(13).Bold().FontColor(Brand);
                col.Item().AlignCenter().Text(
                        $"Verificación de identidad - FLIT  ·  Radicado: {d.Radicado}  ·  Titular 1 de 1")
                    .FontSize(7.5f).FontColor(Muted);
            });
            row.ConstantItem(86); // contrapeso del logo: centra el título
        });

    private static void Solicitud(IContainer c, IdentityLetterData d) =>
        c.Border(0.75f).BorderColor(Brand).Background(BrandBg).Padding(6).Column(col =>
        {
            col.Spacing(2);
            col.Item().Text("SOLICITUD").Bold().FontSize(8).FontColor(Brand);
            col.Item().DefaultTextStyle(s => s.FontSize(7)).Text(t =>
            {
                t.Span("Proyecto: ").FontColor(Muted);
                t.Span(Val(d.CompanyName)).SemiBold();
                t.Span("    Referencia: ").FontColor(Muted);
                t.Span(Val(d.Reference)).SemiBold();
                t.Span("    Emitida: ").FontColor(Muted);
                t.Span(FlitDocumentDate.Format(d.IssuedAt)).SemiBold();
            });
        });

    private static void RoleBand(IContainer c, IdentityLetterData d) =>
        c.Background(Brand).PaddingVertical(5).PaddingHorizontal(10).Row(row =>
        {
            row.RelativeItem().Text(Val(d.RoleLabel)).Bold().FontSize(11).FontColor(Colors.White);
            row.AutoItem().Text("VALIDADO").Bold().FontSize(11).FontColor(Colors.White);
        });

    private static void Datos(IContainer c, IdentityLetterData d) =>
        c.PaddingHorizontal(6).Column(col =>
        {
            col.Spacing(2);
            Row(col, "Nombre registrado", d.RegisteredName, bold: true);
            Row(col, "Tipo de documento", d.DocumentType, bold: true);
            Row(col, "Número de documento", d.DocumentNumber, bold: true);
            Row(col, "Método", "Revisión manual", bold: true);
            Row(col, "Resultado", "APROBADO", bold: true, valueColor: SuccessInk);
            Row(col, "Fecha de validación", FlitDocumentDate.Format(d.ValidatedAt), bold: true);
            Row(col, "Vigencia", d.ValidUntil is { } vu ? $"Hasta el {FormatoFecha.Calendario(ColombiaTime.DayOf(vu))}" : "-", bold: true);
            Row(col, "Revisado por", "FLIT", bold: true);
        });

    private static void Integridad(IContainer c, IdentityLetterData d) =>
        c.PaddingHorizontal(6).Column(col =>
        {
            col.Spacing(2);
            Row(col, "Serie", d.Serie, mono: true);
            Row(col, "Hash (SHA-256)", d.EvidenceSha256, mono: true);
            Row(col, "Sellado", FlitDocumentDate.Format(d.SealedAt), mono: true);
        });

    private static void Trazabilidad(IContainer c, IdentityLetterData d) =>
        c.PaddingHorizontal(6).Column(col =>
        {
            col.Spacing(2);
            Row(col, "Dirección IP", d.ConsentIp);
            Row(col, "Consentimiento Ley 1581", ConsentLine(d, "Otorgado"));
            Row(col, "Custodia de las imágenes", "FLIT conserva las imágenes de esta validación (selfie, documento y firma).");
        });

    private static void Evidencia(IContainer c, IdentityLetterData d) =>
        c.Row(row =>
        {
            row.Spacing(10);
            Foto(row.RelativeItem(), d.Selfie, "Selfie");
            Foto(row.RelativeItem(), d.IdFront, "Documento (frente)");
            Foto(row.RelativeItem(), d.IdBack, "Documento (reverso)");
        });

    private static void Foto(IContainer c, byte[] image, string caption) =>
        c.Column(col =>
        {
            col.Spacing(2);
            col.Item().Height(108).Border(0.5f).BorderColor(EvidenceBorder).Padding(1).AlignCenter().AlignMiddle().Image(image).FitArea();
            col.Item().Text(caption).FontSize(7).FontColor(Muted);
        });

    private static void Firma(IContainer c, IdentityLetterData d) =>
        c.PaddingHorizontal(2).Column(col =>
        {
            col.Spacing(3);
            var version = string.IsNullOrWhiteSpace(d.ConsentTextVersion) ? "-" : d.ConsentTextVersion!.Trim();
            col.Item().Text($"Autorización versión {version} {ConsentLine(d, "aceptada", withVersion: false)} - Ley 1581 de 2012")
                .FontSize(7).FontColor(Muted);

            // Texto que la persona realmente aceptó en esa versión; si no se conoce, no se inventa: solo queda la versión.
            if (d.Texts is { } texts)
            {
                col.Item().DefaultTextStyle(s => s.FontSize(7)).Text(t =>
                {
                    t.Span("Consentimiento: ").SemiBold();
                    t.Span($"{texts.ConsentBody} {texts.PrivacyLabel}.");
                });
                col.Item().DefaultTextStyle(s => s.FontSize(7)).Text(t =>
                {
                    t.Span($"{texts.AuthorizationTitle}: ").SemiBold();
                    t.Span(texts.AuthorizationText);
                });
            }

            col.Item().PaddingTop(2).Height(46).AlignLeft().Image(d.Signature).FitArea();
            col.Item().Text(Val(d.RegisteredName)).Bold().FontSize(9);
            col.Item().Text("Firma manuscrita").FontSize(7).FontColor(Muted);
        });

    private static void Section(IContainer c, string title) =>
        c.Background(BrandBg).PaddingVertical(3).PaddingHorizontal(6)
            .Text(title).Bold().FontSize(8).FontColor(Brand);

    private static void Row(
        ColumnDescriptor col, string label, string? value, bool bold = false, bool mono = false, string? valueColor = null) =>
        col.Item().Row(row =>
        {
            row.ConstantItem(150).Text(label).FontColor(Muted);
            row.RelativeItem().Text(t =>
            {
                var span = t.Span(Val(value));
                if (mono)
                    span.FontFamily(Mono).FontSize(8);
                if (bold)
                    span.Bold();
                if (valueColor is not null)
                    span.FontColor(valueColor);
            });
        });

    /// <summary>«Otorgado 07/10/2026 08:30 (version manual-ley1581-v2)» — la fecha es la del consentimiento registrado.</summary>
    private static string ConsentLine(IdentityLetterData d, string verbo, bool withVersion = true)
    {
        var fecha = d.ConsentAt is { } at ? FlitDocumentDate.Format(at) : "-";
        var version = string.IsNullOrWhiteSpace(d.ConsentTextVersion) ? "-" : d.ConsentTextVersion!.Trim();
        return withVersion ? $"{verbo} {fecha} (versión {version})" : $"{verbo} {fecha}";
    }

    private static string Val(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}
