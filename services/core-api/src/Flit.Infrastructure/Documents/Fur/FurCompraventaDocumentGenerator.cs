using System.Globalization;
using Flit.Infrastructure.Documents.Branding;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Identity;
using QuestPDF;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Flit.Infrastructure.Documents.Fur;

/// <summary>
/// Contrato de compraventa autogenerado por el sistema (traspaso): declaración, fecha, placa,
/// partes, descripción del vehículo y bloques de firma (sello de identidad o baúl).
/// <para><b>ADR-0053</b>: lleva el membrete FLIT compartido (<see cref="FlitLetterhead"/>), igual que
/// Mandato y Solicitud de trámite virtual. El pie con el nombre del documento lo estampa el
/// consolidado (<see cref="FlitPdfStamper"/>), no este generador.</para>
/// </summary>
public static class FurCompraventaDocumentGenerator
{
    private static readonly string[] MesesEs =
        ["Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic"];

    private const float CellGapCm = 0.1f;
    private const int VbCell = FlitRoundedCells.VbCell;

    /// <summary>
    /// Alto reservado para la estampa (baúl o, más adelante, imagen de firma). ~2–3 líneas a 9 pt,
    /// sin línea horizontal: la guía de negocio deja aire abierto bajo el título del bloque.
    /// </summary>
    private const float FirmaEstampaAlto = 32f;

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
                // ADR-0053 — Carta + bandas de membrete; contenido flush con las bandas para que
                // título, tabla de chips y ambas firmas quepan en UNA hoja (mismo criterio que
                // Mandato HU #11034: se compacta caja, no el texto legal).
                FlitLetterhead.ApplyTo(page);
                page.DefaultTextStyle(t => t
                    .FontFamily(FlitDocumentTheme.FontRegular)
                    .FontSize(9)
                    .FontColor(Colors.Black));

                // Cuerpo con aire normal arriba; el hueco libre de la hoja empuja las firmas al pie
                // (no se compactan los párrafos para “llenar” la página).
                FlitLetterhead.Content(page, FlitDocumentTheme.MarginCm, 0f).Column(col =>
                {
                    col.Item().Column(cuerpo =>
                    {
                        cuerpo.Spacing(6);

                        cuerpo.Item().AlignCenter().Text("Contrato de Compraventa")
                            .Bold().FontSize(14).FontColor(FlitDocumentTheme.DarkNavy);

                        cuerpo.Item().PaddingTop(6).Text(t =>
                        {
                            t.Span("Fecha: ");
                            t.Span(FechaLarga(data.FechaTramite));
                        });

                        cuerpo.Item().Text(t =>
                        {
                            t.Span("Ref. ");
                            t.Span($"PLACA: {Val(data.Placa)}")
                                .Bold()
                                .FontColor(FlitDocumentTheme.PrimaryBlue);
                        });

                        cuerpo.Item().PaddingTop(6).Text(
                            "“Contrato de compraventa, documento o declaración en la que conste la transferencia "
                            + "del derecho de dominio del vehículo celebrado con las exigencias de las normas civiles "
                            + "y/o mercantiles”").Justify();

                        cuerpo.Item().PaddingTop(6).Text(t =>
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

                        TablaVehiculo(cuerpo, data);

                        cuerpo.Item().PaddingTop(6).Text(
                            "Lo anterior también se encuentra avalado a través de las firmas puestas en el formulario "
                            + "de solicitud de trámite adjunto al presente.").Justify();
                    });

                    col.Item().Extend().AlignBottom().PaddingBottom(6).Column(firmas =>
                    {
                        firmas.Item().Text("Cordialmente,");
                        BloqueFirma(firmas, "FIRMA DEL PROPIETARIO ACTUAL", data, vendedor, "vendedor");
                        BloqueFirma(firmas, "FIRMA DEL NUEVO PROPIETARIO", data, comprador, "comprador");
                    });
                });
            });
        }).GeneratePdf();
    }

    // Grilla 2×3 tipo chip (cabecera azul + valor claro), mismo patrón que SOAT/RTM.
    private static void TablaVehiculo(ColumnDescriptor col, FurDocumentData data)
    {
        col.Item().PaddingTop(8).AlignCenter().Text("DESCRIPCIÓN DEL VEHICULO")
            .Bold().FontSize(10).FontColor(FlitDocumentTheme.DarkNavy);

        col.Item().Column(tabla =>
        {
            tabla.Spacing(CellGapCm, Unit.Centimetre);
            tabla.Item().Row(row =>
            {
                row.Spacing(CellGapCm, Unit.Centimetre);
                Chip(row.RelativeItem(), "Marca", Val(data.Vehiculo.Marca), roundLeft: true, roundRight: false);
                Chip(row.RelativeItem(), "Chasis", Val(data.Vehiculo.NumeroChasis), roundLeft: false, roundRight: false);
                Chip(row.RelativeItem(), "Motor", Val(data.Vehiculo.NumeroMotor), roundLeft: false, roundRight: true);
            });
            tabla.Item().Row(row =>
            {
                row.Spacing(CellGapCm, Unit.Centimetre);
                Chip(row.RelativeItem(), "Modelo", Val(data.Vehiculo.Modelo), roundLeft: true, roundRight: false);
                Chip(row.RelativeItem(), "VIN", Val(data.Vin), roundLeft: false, roundRight: false);
                Chip(row.RelativeItem(), "Referencia", Val(data.Vehiculo.Linea), roundLeft: false, roundRight: true);
            });
        });
    }

    private static void Chip(IContainer container, string label, string? value, bool roundLeft, bool roundRight)
    {
        container.Column(c =>
        {
            FlitRoundedCells.Cell(
                c.Item(),
                FlitRoundedCells.HeaderBg,
                tl: roundLeft,
                tr: roundRight,
                br: false,
                bl: false,
                VbCell,
                inner => inner.PaddingHorizontal(6).AlignCenter().AlignMiddle()
                    .Text(label).Bold().FontSize(8).FontColor(FlitRoundedCells.White));
            FlitRoundedCells.Cell(
                c.Item(),
                FlitRoundedCells.ValueBg,
                tl: false,
                tr: false,
                br: roundRight,
                bl: roundLeft,
                VbCell,
                inner => inner.PaddingHorizontal(6).AlignCenter().AlignMiddle()
                    .Text(Val(value)).FontSize(8).FontColor(FlitDocumentTheme.DarkNavy));
        });
    }

    // Bloque de firma de una parte: título, hueco de estampa (sin línea), identificación y sello.
    // Sin firma validada el hueco queda en blanco (no bloquea).
    private static void BloqueFirma(
        ColumnDescriptor col, string titulo, FurDocumentData data, DocumentParte? parte, string rol)
    {
        col.Item().PaddingTop(8).Text(titulo)
            .Bold()
            .FontColor(FlitDocumentTheme.DarkNavy);

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

        var firmaIdentidad =
            firmaBaul is null
            && data.FirmaIdentidadImagenes is not null
            && data.FirmaIdentidadImagenes.TryGetValue(rol, out var recorte)
            && recorte.Length > 0
            && IdentitySignatureImageFormat.IsSupported(recorte)
                ? recorte
                : null;

        var hueco = col.Item().PaddingTop(2).Height(FirmaEstampaAlto);
        if (firmaBaul is not null)
            hueco.Image(firmaBaul).FitHeight();
        else if (firmaIdentidad is not null)
            hueco.Image(firmaIdentidad).FitHeight();

        if (parte is null)
            return;

        if (parte.EsJuridica)
        {
            DatoFirmante(col, $"Razón Social: {Val(parte.Nombre)}");
            DatoFirmante(col, $"NIT: {Val(parte.Documento)}");
            // Quien firma por la empresa es su REPRESENTANTE LEGAL, así que el bloque tiene que
            // identificarlo: sin su nombre y documento la firma no queda atribuida a nadie. Los datos ya
            // viajan en DocumentParte (ADR-0036); antes simplemente no se imprimían aquí.
            DatoFirmante(col, $"Representante legal: {Val(parte.RepresentanteLegalNombre)}");
            DatoFirmante(col, $"{TipoDocRepresentante(parte)}: {Val(parte.RepresentanteLegalDocumento)}");
        }
        else
        {
            DatoFirmante(col, $"Nombre: {Val(parte.Nombre)}");
            DatoFirmante(col, $"{TipoDocParte(parte)}: {Val(parte.Documento)}");
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

    private static void DatoFirmante(ColumnDescriptor col, string linea) =>
        col.Item().Text(linea).Bold().FontSize(9).FontColor(FlitDocumentTheme.DarkNavy);

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
