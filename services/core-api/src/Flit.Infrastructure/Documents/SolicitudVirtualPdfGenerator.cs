using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Tramites.Catalog;
using QuestPDF;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Generador de la <b>Solicitud de trámite de forma virtual</b> (ADR-0036, HU #10914). Porta el
/// formato legacy <c>virtual-process/*.hbs</c> a QuestPDF (tipo <c>tramite_virtual</c>), fusionable al
/// Expediente Consolidado (mismo patrón que <see cref="FurCompraventaDocumentGenerator"/>). Solo varía
/// el firmante: persona natural a nombre propio; persona jurídica su representante legal en nombre de
/// la empresa. El texto legal (Resoluciones 12379/2012 y 20233040017145/2023) se transcribe literal.
/// Las firmas/datos del firmante solo se pintan en estado distinto de borrador
/// (<see cref="FurDocumentData.FirmasVisibles"/>).
/// </summary>
public sealed class SolicitudVirtualPdfGenerator : ISolicitudVirtualGenerator
{
    static SolicitudVirtualPdfGenerator()
    {
        Settings.License = LicenseType.Community;
    }

    public GeneratedDocument GenerateSolicitudVirtual(FurDocumentData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var parte = data.Radicador;
        var esJuridica = parte?.EsJuridica ?? false;
        var esTraspaso = string.Equals(
            data.TipologiaCodigo, TramiteTipologiaCatalog.CodigoTraspasoStandard, StringComparison.OrdinalIgnoreCase);
        var tramite = esTraspaso ? "TRASPASO DE PROPIEDAD" : "MATRÍCULA INICIAL";

        var ciudad = Val(data.Organismo.Ciudad, "___");
        var fecha = FormatFechaEs(data.FechaTramite ?? DateTime.UtcNow.AddHours(-5));
        var ot = Val(data.Organismo.Nombre, "___");
        var placa = Val(data.Placa, "___");

        var bytes = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(11).FontFamily(Fonts.Arial));

                page.Content().Column(col =>
                {
                    col.Spacing(8);
                    col.Item().Text($"{ciudad}, {fecha}");
                    col.Item().PaddingTop(10).AlignCenter().Text(t => t.Span("SOLICITUD TRÁMITE DE FORMA VIRTUAL").Bold());

                    col.Item().PaddingTop(6).Text(Parrafo1(data, parte, esJuridica, tramite, ot, placa));

                    col.Item().Text(
                        "Por lo anterior, solicito se autorice el trámite indicado y de esta manera aportar los requisitos " +
                        "establecidos por la normatividad vigente para la realización del mismo, aunado, doy fe de que dicha " +
                        "documentación es totalmente legal, por lo que exonero al organismo de tránsito de cualquier tipo de " +
                        "responsabilidad y situación presentada.");

                    col.Item().Text(
                        "Dicha solicitud es realizada teniendo en cuenta la Resolución 20233040017145 de 2023 \"por la cual se " +
                        "modifica la Resolución 20223040045295 de 2022 y se dictan disposiciones para la correcta y amplia " +
                        "implementación de la política de simplificación y racionalización de trámites\", de igual manera, " +
                        "modifica la Resolución 12379 de 2012 \"por medio de la cual se establecen procedimientos y requisitos " +
                        "para realizar trámites ante un organismo de tránsito\".");

                    col.Item().PaddingTop(10).Text("Cordialmente,");

                    if (data.FirmasVisibles)
                    {
                        col.Item().PaddingTop(24).Column(sig =>
                        {
                            foreach (var line in FirmaBlock(parte, esJuridica))
                                sig.Item().Text(t => t.Span(line).Bold().FontSize(10));
                        });
                    }
                });
            });
        }).GeneratePdf();

        return new GeneratedDocument(
            "tramite_virtual",
            $"solicitud_tramite_virtual_{SafeRef(data.ReferenceNumber)}.pdf",
            "application/pdf",
            bytes);
    }

    private static string Parrafo1(
        FurDocumentData data, DocumentParte? parte, bool esJuridica, string tramite, string ot, string placa)
    {
        if (esJuridica)
        {
            var rlNombre = Val(parte?.RepresentanteLegalNombre, "___");
            var rlTipo = MapDoc(parte?.RepresentanteLegalTipoDoc);
            var rlDoc = Val(parte?.RepresentanteLegalDocumento, "___");
            var empresa = Val(parte?.Nombre, "___");
            var nit = Val(parte?.Documento, "___");
            return $"Yo, {rlNombre}, identificado con {rlTipo} No. {rlDoc}, en calidad de representación de la empresa " +
                $"{empresa} con número NIT {nit}, propietario del automotor de placa {placa}, me permito indicar que hoy " +
                $"se realizará el trámite de {tramite} ante el organismo de tránsito {ot} respecto del automotor, dicho " +
                "trámite será realizado por: propietario ( ),  un tercero (X), el cual aportará el correspondiente poder o " +
                "contrato de mandato a la documentación del trámite.";
        }

        var nombre = Val(parte?.Nombre, "___");
        var tipo = MapDoc(parte?.DocumentType);
        var docu = Val(parte?.Documento, "___");
        return $"Yo, {nombre}, identificado con {tipo} No. {docu}, propietario del automotor de placa {placa}, me permito " +
            $"indicar que hoy se realizará el trámite de {tramite} ante el organismo de tránsito {ot} respecto de mi " +
            "automotor, dicho trámite será realizado por: propietario ( ),  un tercero (X), el cual aportará el " +
            "correspondiente poder o contrato de mandato a la documentación del trámite.";
    }

    private static IEnumerable<string> FirmaBlock(DocumentParte? parte, bool esJuridica)
    {
        if (esJuridica)
        {
            yield return $"EMPRESA: {Val(parte?.Nombre, "___")}";
            yield return $"{MapDoc(parte?.DocumentType).ToUpperInvariant()}: {Val(parte?.Documento, "___")}";
            yield return $"NOMBRE: {Val(parte?.RepresentanteLegalNombre, "___")}";
            yield return $"{MapDoc(parte?.RepresentanteLegalTipoDoc).ToUpperInvariant()}: {Val(parte?.RepresentanteLegalDocumento, "___")}";
        }
        else
        {
            yield return $"NOMBRE: {Val(parte?.Nombre, "___")}";
            yield return $"{MapDoc(parte?.DocumentType).ToUpperInvariant()}: {Val(parte?.Documento, "___")}";
        }

        yield return $"CELULAR: {Val(parte?.Phone, "___")}";
        yield return $"CORREO ELECTRÓNICO: {Val(parte?.Email, "___")}";
    }

    // Nombres de mes en español SIN depender de una cultura instalada (el runtime puede correr en
    // globalization-invariant mode, donde "es-CO" no existe).
    private static readonly string[] MesesEs =
    [
        "enero", "febrero", "marzo", "abril", "mayo", "junio",
        "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre",
    ];

    private static string FormatFechaEs(DateTime fecha) =>
        $"{fecha.Day} de {MesesEs[fecha.Month - 1]} de {fecha.Year}";

    private static string MapDoc(string? code) => (code?.Trim().ToUpperInvariant()) switch
    {
        "CC" => "Cédula de Ciudadanía",
        "CE" => "Cédula de Extranjería",
        "NIT" => "NIT",
        "PA" or "PAS" => "Pasaporte",
        "TI" => "Tarjeta de Identidad",
        null or "" => "documento",
        _ => code!.Trim(),
    };

    private static string Val(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string SafeRef(string? reference) =>
        string.IsNullOrWhiteSpace(reference)
            ? "sin_ref"
            : new string(reference.Trim().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
}
