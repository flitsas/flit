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

    static FurCompraventaDocumentGenerator()
    {
        Settings.License = LicenseType.Community;
    }

    public static byte[] Generate(FurDocumentData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        FlitFonts.EnsureRegistered();

        var vendedores = FurCompraventaCopropiedad.DelRol(data.Partes, "vendedor");
        var compradores = FurCompraventaCopropiedad.DelRol(data.Partes, "comprador");
        var copropiedad = FurCompraventaCopropiedad.EsMultiple(vendedores)
            || FurCompraventaCopropiedad.EsMultiple(compradores);

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
                // (no se compactan los párrafos para “llenar” la página). Con copropiedad las firmas
                // van en fila justo bajo el cuerpo para no saltar de hoja.
                FlitLetterhead.Content(page, FlitDocumentTheme.MarginCm, 0f).Column(col =>
                {
                    col.Item().Column(cuerpo =>
                    {
                        cuerpo.Spacing(copropiedad ? 4 : 6);

                        cuerpo.Item().AlignCenter().Text("Contrato de Compraventa")
                            .Bold().FontSize(14).FontColor(FlitDocumentTheme.DarkNavy);

                        cuerpo.Item().PaddingTop(copropiedad ? 4 : 6).Text(t =>
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

                        cuerpo.Item().PaddingTop(copropiedad ? 4 : 6).Text(
                            "“Contrato de compraventa, documento o declaración en la que conste la transferencia "
                            + "del derecho de dominio del vehículo celebrado con las exigencias de las normas civiles "
                            + "y/o mercantiles”").Justify();

                        cuerpo.Item().PaddingTop(copropiedad ? 4 : 6).Text(t =>
                        {
                            t.Justify();
                            t.Span("Por el presente documento se hace constar la voluntad expresa que tiene por un lado ");
                            PintarIdentificacion(t, vendedores);
                            t.Span(" en su condición de "
                                + FurCompraventaCopropiedad.CondicionPropietario(vendedores)
                                + " de transferir la propiedad del vehículo de la placa de la referencia a ");
                            PintarIdentificacion(t, compradores);
                            t.Span(", en razón a un negocio jurídico de compraventa, "
                                + "realizado entre ambas partes. Negocio que tuvo un valor de ");
                            t.Span($"$ {Moneda(data.ValorVenta)}").Bold();
                        });

                        TablaVehiculo(cuerpo, data);

                        cuerpo.Item().PaddingTop(copropiedad ? 4 : 6).Text(
                            "Lo anterior también se encuentra avalado a través de las firmas puestas en el formulario "
                            + "de solicitud de trámite adjunto al presente.").Justify();
                    });

                    var firmasSlot = copropiedad
                        ? col.Item().PaddingTop(4)
                        : col.Item().Extend().AlignBottom().PaddingBottom(6);
                    firmasSlot.Column(firmas =>
                    {
                        firmas.Item().Text("Cordialmente,");
                        LadoFirmas(
                            firmas,
                            "FIRMA DEL PROPIETARIO ACTUAL",
                            "FIRMAS DE LOS PROPIETARIOS ACTUALES",
                            data,
                            vendedores,
                            "vendedor");
                        LadoFirmas(
                            firmas,
                            "FIRMA DEL NUEVO PROPIETARIO",
                            "FIRMAS DE LOS NUEVOS PROPIETARIOS",
                            data,
                            compradores,
                            "comprador");
                    });
                });
            });
        }).GeneratePdf();
    }

    private static void PintarIdentificacion(TextDescriptor t, List<DocumentParte> partes)
    {
        foreach (var (text, bold) in FurCompraventaCopropiedad.Identificacion(partes))
        {
            if (bold)
                t.Span(text).Bold();
            else
                t.Span(text);
        }
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

    private static void LadoFirmas(
        ColumnDescriptor col,
        string tituloSingular,
        string tituloPlural,
        FurDocumentData data,
        List<DocumentParte> partes,
        string rol)
    {
        if (!FurCompraventaCopropiedad.EsMultiple(partes))
        {
            BloqueFirma(
                col,
                tituloSingular,
                data,
                partes.Count == 1 ? partes[0] : null,
                rol,
                compact: false,
                FirmaEstampaAlto);
            return;
        }

        col.Item().PaddingTop(6).Text(tituloPlural)
            .Bold()
            .FontColor(FlitDocumentTheme.DarkNavy);

        var alto = FurCompraventaCopropiedad.EstampaAlto(partes.Count);
        col.Item().PaddingTop(2).Row(row =>
        {
            row.Spacing(6);
            foreach (var parte in partes)
            {
                row.RelativeItem().Column(c =>
                    BloqueFirma(c, titulo: null, data, parte, rol, compact: true, alto));
            }
        });
    }

    // Bloque de firma de una parte: título, hueco de estampa (sin línea), identificación y sello.
    // Sin firma validada el hueco queda en blanco (no bloquea).
    private static void BloqueFirma(
        ColumnDescriptor col,
        string? titulo,
        FurDocumentData data,
        DocumentParte? parte,
        string rol,
        bool compact,
        float estampaAlto)
    {
        if (!string.IsNullOrEmpty(titulo))
        {
            col.Item().PaddingTop(8).Text(titulo)
                .Bold()
                .FontColor(FlitDocumentTheme.DarkNavy);
        }

        // Bug #11146 — la imagen del baúl y el sello de identidad son EXCLUYENTES: una parte firma de
        // una sola manera. Antes se pintaba la imagen y, más abajo, el sello sin condición alguna, así
        // que quien firmaba por el baúl y además tenía identidad vigente aparecía firmando dos veces
        // por vías distintas. El mandato y la solicitud de trámite virtual ya lo resolvían así.
        var firmaKey = parte is null ? rol : FurCompraventaCopropiedad.FirmaKey(parte);
        var firmaBaul = ImagenDe(data.FirmaImagenes, firmaKey, rol);
        var firmaIdentidad = firmaBaul is null
            ? ImagenDe(data.FirmaIdentidadImagenes, firmaKey, rol)
            : null;
        if (firmaIdentidad is not null && !IdentitySignatureImageFormat.IsSupported(firmaIdentidad))
            firmaIdentidad = null;

        var hueco = col.Item().PaddingTop(2).Height(estampaAlto);
        if (firmaBaul is not null)
            hueco.Image(firmaBaul).FitHeight();
        else if (firmaIdentidad is not null)
            hueco.Image(firmaIdentidad).FitHeight();

        if (parte is null)
            return;

        var fontDato = compact ? 7f : 9f;
        if (parte.EsJuridica)
        {
            DatoFirmante(col, $"Razón Social: {Val(parte.Nombre)}", fontDato);
            DatoFirmante(col, $"NIT: {Val(parte.Documento)}", fontDato);
            if (!compact)
            {
                // Quien firma por la empresa es su REPRESENTANTE LEGAL, así que el bloque tiene que
                // identificarlo: sin su nombre y documento la firma no queda atribuida a nadie. Los datos ya
                // viajan en DocumentParte (ADR-0036); antes simplemente no se imprimían aquí.
                DatoFirmante(col, $"Representante legal: {Val(parte.RepresentanteLegalNombre)}", fontDato);
                DatoFirmante(col, $"{TipoDocRepresentante(parte)}: {Val(parte.RepresentanteLegalDocumento)}", fontDato);
            }
            else
            {
                DatoFirmante(
                    col,
                    $"RL: {Val(parte.RepresentanteLegalNombre)} {TipoDocRepresentante(parte)} {Val(parte.RepresentanteLegalDocumento)}",
                    6.5f);
            }
        }
        else
        {
            DatoFirmante(col, $"Nombre: {Val(parte.Nombre)}", fontDato);
            DatoFirmante(col, $"{TipoDocParte(parte)}: {Val(parte.Documento)}", fontDato);
        }

        // Trazabilidad de la firma, en el mismo lugar sea cual sea el mecanismo:
        //  · Con firma del baúl, esa ES la firma (Bug #11146): el sello de identidad no se añade, y en su
        //    lugar va la vigencia y el hash de la firma custodiada (HU #11170). Antes no iba nada, así
        //    que la imagen quedaba sin ningún dato que permitiera verificarla —la carencia solo se hizo
        //    visible al retirar el sello de identidad que se pintaba de más—.
        //  · Sin ella, el sello de la validación biométrica.
        var sello = firmaBaul is null
            ? Sello(data, firmaKey, rol)
            : FlitFirmaBaulSello.Resolve(data.FirmaBaulMetadatos, firmaKey, incluirIdentificacion: false)
              ?? FlitFirmaBaulSello.Resolve(data.FirmaBaulMetadatos, rol, incluirIdentificacion: false);
        if (sello is not null)
            col.Item().Text(sello).FontSize(compact ? 5.5f : 6.5f).FontColor(Colors.Grey.Darken2);
    }

    private static byte[]? ImagenDe(
        IReadOnlyDictionary<string, byte[]>? dict, string key, string rolFallback)
    {
        if (dict is null)
            return null;
        if (dict.TryGetValue(key, out var imagen) && imagen.Length > 0)
            return imagen;
        if (!string.Equals(key, rolFallback, StringComparison.Ordinal)
            && dict.TryGetValue(rolFallback, out imagen)
            && imagen.Length > 0)
            return imagen;
        return null;
    }

    private static void DatoFirmante(ColumnDescriptor col, string linea, float fontSize) =>
        col.Item().Text(linea).Bold().FontSize(fontSize).FontColor(FlitDocumentTheme.DarkNavy);

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

    private static string? Sello(FurDocumentData data, string key, string rolFallback)
    {
        if (!data.IdentidadValidada || data.SellosIdentidad is null)
            return null;
        if (data.SellosIdentidad.TryGetValue(key, out var sello) && !string.IsNullOrWhiteSpace(sello))
            return sello;
        if (!string.Equals(key, rolFallback, StringComparison.Ordinal)
            && data.SellosIdentidad.TryGetValue(rolFallback, out sello)
            && !string.IsNullOrWhiteSpace(sello))
            return sello;
        return null;
    }

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
