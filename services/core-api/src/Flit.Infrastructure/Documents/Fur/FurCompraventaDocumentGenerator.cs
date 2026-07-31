using System.Globalization;
using Flit.Infrastructure.Documents.Branding;
using Flit.Tramites.Application.Documents;
using QuestPDF;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Flit.Infrastructure.Documents.Fur;

/// <summary>
/// Contrato de compraventa autogenerado por el sistema (traspaso), con el contenido y la disposición de
/// la muestra oficial (recursos dllo membrete/PDF/compraventa-generado-sistema.pdf): texto de la
/// declaración, fecha, placa de referencia, párrafo con las partes y sus casillas de tipo de documento,
/// descripción del vehículo y bloques de firma con el sello de validación de identidad.
/// <para><b>Sin encabezados personalizados</b> (decisión de negocio): este documento NO lleva el membrete
/// FLIT ni pie de marca — es una declaración legal que debe presentarse limpia.</para>
/// </summary>
public static class FurCompraventaDocumentGenerator
{
    private static readonly string[] MesesEs =
        ["Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic"];

    // Tipos de documento que se marcan con casilla en el párrafo de cada parte (orden de la muestra).
    private static readonly (string Etiqueta, string[] Codigos)[] TiposDocumento =
    [
        ("NIT", ["NIT", "N"]),
        ("C.C.", ["CC", "C", "C.C."]),
        ("C.E.", ["CE", "E", "C.E."]),
        ("T.I", ["TI", "T", "T.I"]),
        ("P.A", ["PA", "P", "PAS", "P.A"]),
    ];

    static FurCompraventaDocumentGenerator()
    {
        Settings.License = LicenseType.Community;
    }

    public static byte[] Generate(FurDocumentData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        FlitFonts.EnsureRegistered();

        var vendedor = Parte(data, "vendedor");
        var comprador = Parte(data, "comprador");

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.Letter);
                // Márgenes ajustados (vertical más corto) para que el contenido quepa en UNA página con
                // Poppins (más ancha que la Arial de la muestra) aun con los sellos/firmas de ambas partes.
                page.MarginVertical(1.5f, Unit.Centimetre);
                page.MarginHorizontal(2.5f, Unit.Centimetre);
                page.DefaultTextStyle(t => t
                    .FontFamily(FlitDocumentTheme.FontRegular)
                    .FontSize(9)
                    .FontColor(Colors.Black));
                // Sin Header/Footer: el documento no lleva encabezados personalizados.

                page.Content().Column(col =>
                {
                    col.Spacing(6);

                    col.Item().Text(
                        "“Contrato de compraventa, documento o declaración en la que conste la transferencia "
                        + "del derecho de dominio del vehículo celebrado con las exigencias de las normas civiles "
                        + "y/o mercantiles”").Justify();

                    col.Item().PaddingTop(6).Text(t =>
                    {
                        t.Span("Fecha: ");
                        t.Span($"  {FechaLarga(data.FechaTramite)}  ").Underline();
                    });

                    col.Item().Text(t =>
                    {
                        t.Span("Ref. ");
                        t.Span($"PLACA: {Val(data.Placa)}").Bold();
                    });

                    col.Item().PaddingTop(6).Text(t =>
                    {
                        t.Justify();
                        t.Span("Por el presente documento se hace constar la voluntad expresa que tiene por un lado ");
                        t.Span(Val(vendedor?.Nombre)).Bold();
                        t.Span(", identificado(a) con ");
                        t.Span(Casillas(vendedor?.DocumentType));
                        t.Span($" {Val(vendedor?.Documento)} en su condición de propietario(a) inscrito(a) de "
                            + "transferir la propiedad del vehículo de la placa de la referencia a ");
                        t.Span(Val(comprador?.Nombre)).Bold();
                        t.Span(", identificado(a) con ");
                        t.Span(Casillas(comprador?.DocumentType));
                        t.Span($" {Val(comprador?.Documento)}, en razón a un negocio jurídico de compraventa, "
                            + "realizado entre ambas partes. Negocio que tuvo un valor de ");
                        t.Span($"$ {Moneda(data.ValorVenta)}").Bold();
                    });

                    col.Item().PaddingTop(8).Text("DESCRIPCIÓN DEL VEHICULO").Bold();
                    col.Item().Text($"Marca: {Val(data.Vehiculo.Marca)}        "
                        + $"Chasis: {Val(data.Vehiculo.NumeroChasis)}        "
                        + $"Motor: {Val(data.Vehiculo.NumeroMotor)}");
                    col.Item().Text($"Modelo: {Val(data.Vehiculo.Modelo)}        "
                        + $"VIN: {Val(data.Vin)}        "
                        + $"Referencia: {Val(data.Vehiculo.Linea)}");

                    col.Item().PaddingTop(6).Text(
                        "Lo anterior también se encuentra avalado a través de las firmas puestas en el formulario "
                        + "de solicitud de trámite adjunto al presente.").Justify();

                    col.Item().PaddingTop(6).Text("Cordialmente,");

                    BloqueFirma(col, "FIRMA DEL PROPIETARIO ACTUAL", data, vendedor, "vendedor");
                    BloqueFirma(col, "FIRMA DEL NUEVO PROPIETARIO", data, comprador, "comprador");
                });
            });
        }).GeneratePdf();
    }

    // Bloque de firma de una parte: título, imagen de la firma (si el baúl la resolvió), identificación y
    // sello de validación de identidad. Sin firma validada se deja el espacio en blanco (no bloquea).
    private static void BloqueFirma(
        ColumnDescriptor col, string titulo, FurDocumentData data, DocumentParte? parte, string rol)
    {
        col.Item().PaddingTop(8).Text(titulo);

        // Bug #11146 — la imagen del baúl y el sello de identidad son EXCLUYENTES: una parte firma de
        // una sola manera. Antes se pintaba la imagen y, más abajo, el sello sin condición alguna, así
        // que quien firmaba por el baúl y además tenía identidad vigente aparecía firmando dos veces
        // por vías distintas. El mandato y la solicitud de trámite virtual ya lo resolvían así.
        var firmaBaul =
            data.FirmaImagenes is not null
            && data.FirmaImagenes.TryGetValue(rol, out var imagen)
            && imagen.Length > 0
                ? imagen
                : null;

        if (firmaBaul is not null)
        {
            col.Item().PaddingTop(2).Height(32).Image(firmaBaul).FitHeight();
        }
        else
        {
            col.Item().PaddingTop(12).Width(240).LineHorizontal(0.5f);
        }

        if (parte is null)
            return;

        if (parte.EsJuridica)
        {
            col.Item().Text($"Razón Social: {Val(parte.Nombre)}").FontSize(9);
            col.Item().Text($"NIT: {Val(parte.Documento)}").FontSize(9);
            // Quien firma por la empresa es su REPRESENTANTE LEGAL, así que el bloque tiene que
            // identificarlo: sin su nombre y documento la firma no queda atribuida a nadie. Los datos ya
            // viajan en DocumentParte (ADR-0036); antes simplemente no se imprimían aquí.
            col.Item().Text($"Representante legal: {Val(parte.RepresentanteLegalNombre)}").FontSize(9);
            col.Item()
                .Text($"{TipoDocRepresentante(parte)}: {Val(parte.RepresentanteLegalDocumento)}")
                .FontSize(9);
        }
        else
        {
            col.Item().Text($"Nombre: {Val(parte.Nombre)}").FontSize(9);
            col.Item().Text($"{TipoDocParte(parte)}: {Val(parte.Documento)}").FontSize(9);
        }

        // Trazabilidad de la firma, en el mismo lugar sea cual sea el mecanismo:
        //  · Con firma del baúl, esa ES la firma (Bug #11146): el sello de identidad no se añade, y en su
        //    lugar va la vigencia y el hash de la firma custodiada (HU #11170). Antes no iba nada, así
        //    que la imagen quedaba sin ningún dato que permitiera verificarla —la carencia solo se hizo
        //    visible al retirar el sello de identidad que se pintaba de más—.
        //  · Sin ella, el sello de la validación biométrica.
        var sello = firmaBaul is null
            ? Sello(data, rol)
            : FlitFirmaBaulSello.Resolve(data.FirmaBaulMetadatos, rol, incluirIdentificacion: false);
        if (sello is not null)
            col.Item().Text(sello).FontSize(6.5f).FontColor(Colors.Grey.Darken2);
    }

    // Casillas de tipo de documento: se marca con [X] la del tipo de la parte y el resto en blanco.
    private static string Casillas(string? documentType)
    {
        var tipo = (documentType ?? string.Empty).Trim().ToUpperInvariant().Replace(".", "", StringComparison.Ordinal);
        return string.Join("   ", TiposDocumento.Select(t =>
        {
            var marcada = t.Codigos.Any(c => string.Equals(c.Replace(".", "", StringComparison.Ordinal), tipo, StringComparison.Ordinal));
            return $"{t.Etiqueta} [{(marcada ? "X" : " ")}]";
        }));
    }

    /// <summary>
    /// Rótulo del documento de la parte natural ("C.C.", "Documento"…). Se usa el tipo real cuando
    /// viene; sin él, un rótulo neutro en vez de suponer cédula.
    /// </summary>
    private static string TipoDocParte(DocumentParte parte) =>
        string.IsNullOrWhiteSpace(parte.DocumentType) ? "Documento" : parte.DocumentType.Trim();

    /// <summary>Rótulo del documento del representante legal, con el mismo criterio.</summary>
    private static string TipoDocRepresentante(DocumentParte parte) =>
        string.IsNullOrWhiteSpace(parte.RepresentanteLegalTipoDoc)
            ? "Documento"
            : parte.RepresentanteLegalTipoDoc.Trim();

    private static DocumentParte? Parte(FurDocumentData data, string rol) =>
        data.Partes.FirstOrDefault(p => string.Equals(p.Rol, rol, StringComparison.OrdinalIgnoreCase));

    // Sello de identidad de la parte, solo si la identidad está validada y hay sello para el rol.
    private static string? Sello(FurDocumentData data, string rol) =>
        data.IdentidadValidada
        && data.SellosIdentidad is not null
        && data.SellosIdentidad.TryGetValue(rol, out var sello)
        && !string.IsNullOrWhiteSpace(sello)
            ? sello
            : null;

    // Fecha "22 Jul 2026" sin depender de la cultura (el contenedor corre en modo invariante).
    private static string FechaLarga(DateTime? fecha)
    {
        var f = fecha ?? DateTime.UtcNow;
        return $"{f.Day:00} {MesesEs[f.Month - 1]} {f.Year}";
    }

    private static string Moneda(decimal? valor) =>
        valor is null ? string.Empty : valor.Value.ToString("#,##0", CultureInfo.InvariantCulture);

    private static string Val(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
