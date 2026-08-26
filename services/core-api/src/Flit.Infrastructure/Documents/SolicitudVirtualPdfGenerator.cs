using Flit.Infrastructure.Documents.Branding;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Documents;
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
/// <para><b>Las firmas se pintan en todos los estados.</b> Ocultarlas en borrador (punto 18 del
/// requerimiento de ADR-0036) dejaba la solicitud terminando en «Cordialmente,» y nada debajo: ni
/// línea, ni nombre, ni documento. Lo que aún no exista sale vacío, que es lo correcto en un
/// borrador.</para>
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

        // HU #11032 — la solicitud la firma el PROPIETARIO del vehículo: en un traspaso, quien vende.
        // Antes salía a nombre del radicador (el comprador), declarando por la parte equivocada.
        var parte = data.Propietario;
        var esJuridica = parte?.EsJuridica ?? false;
        // ADR-0050 — el rótulo legal es el NOMBRE del tipo. Se resuelve con el mismo componente que
        // el mandato: dos documentos del mismo expediente no pueden nombrar el trámite distinto.
        var tramite = MandatoTramiteIdentity.NombreObjeto(
            data.ProcedureTypeName,
            data.ProcedureTypeCode,
            data.ProcedureFamily,
            data.TipologiaCodigo,
            data.Modalidad);

        // HU #11016 — sin ciudad legible el encabezado es solo la fecha: antes se imprimía el código
        // DIVIPOLA del organismo («25286, 28 de julio de 2026»), que parecía pegado a la fecha.
        var ciudad = data.Organismo.Ciudad?.Trim();
        var fecha = FormatFechaEs(data.FechaTramite ?? DateTime.UtcNow.AddHours(-5));
        var ot = Val(data.Organismo.Nombre, "___");
        var placa = Val(data.Placa, "___");

        var bytes = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                // HU #11033 — membrete institucional FLIT (HU #10856), igual que los certificados
                // generados. Sustituye el A4 con márgenes sueltos por la página con bandas del
                // membrete arriba y abajo, y el contenido dentro del margen FLIT.
                FlitLetterhead.ApplyTo(page);
                page.DefaultTextStyle(t => t.FontSize(11).FontFamily(FlitDocumentTheme.FontRegular));

                FlitLetterhead.Content(page).Column(col =>
                {
                    col.Spacing(8);
                    col.Item().Text(string.IsNullOrEmpty(ciudad) ? fecha : $"{ciudad}, {fecha}");
                    col.Item().PaddingTop(10).AlignCenter().Text(t => t.Span("SOLICITUD TRÁMITE DE FORMA VIRTUAL").Bold());

                    RenderKeywordText(
                        col.Item().PaddingTop(6),
                        Parrafo1(data, parte, esJuridica, tramite, ot, placa),
                        Parrafo1Keywords(parte, esJuridica, tramite, placa));

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

                    // HU #11046 — la estampa (imagen del baúl o sello de identidad) va SOBRE la línea
                    // y los datos del firmante debajo. FlitFirmaBlock centraliza la composición y la
                    // prioridad del baúl (HU #11031), compartidas con el contrato de mandato.
                    col.Item().PaddingTop(24).Column(sig =>
                        FlitFirmaBlock.Render(
                            sig,
                            FirmaBaulDe(data, parte?.Rol),
                            SelloIdentidadDe(data, parte?.Rol),
                            FirmaBlock(parte, esJuridica),
                            FlitFirmaLinea.Grafica,
                            datosBold: true,
                            // HU #11170 — vigencia y hash de la firma del baúl, como en el FUR: sin
                            // ellos la imagen queda sin nada que permita verificarla.
                            selloBaul: FlitFirmaBaulSello.Resolve(
                                data.FirmaBaulMetadatos, parte?.Rol, incluirIdentificacion: false)));
                });
            });
        }).GeneratePdf();

        return new GeneratedDocument(
            "tramite_virtual",
            $"solicitud_tramite_virtual_{SafeRef(data.ReferenceNumber)}.pdf",
            "application/pdf",
            bytes);
    }

    // HU #10998 — resalta en negrita las palabras clave del párrafo de solicitud (tipo de trámite, placa e
    // identificación del solicitante), replicando el patrón de spans de la compraventa autogenerada.
    private static void RenderKeywordText(IContainer container, string texto, string[] keywords) =>
        container.Text(t =>
        {
            foreach (var (segment, bold) in SplitKeywords(texto, keywords))
            {
                var span = t.Span(segment);
                if (bold)
                    span.Bold();
            }
        });

    private static string[] Parrafo1Keywords(
        DocumentParte? parte, bool esJuridica, string tramite, string placa)
    {
        var keys = new List<string> { tramite, placa };
        if (esJuridica)
        {
            keys.Add(Val(parte?.RepresentanteLegalNombre, "___"));
            keys.Add(Val(parte?.Nombre, "___"));
            keys.Add(Val(parte?.Documento, "___"));
        }
        else
        {
            keys.Add(Val(parte?.Nombre, "___"));
            keys.Add(Val(parte?.Documento, "___"));
        }

        return [.. keys.Where(k => !string.IsNullOrWhiteSpace(k) && k != "___").Distinct()];
    }

    // Divide el texto en segmentos normales y en negrita según coincidencias EXACTAS (case-sensitive) de
    // las palabras clave, tomando siempre la coincidencia más larga en cada posición.
    private static IEnumerable<(string Text, bool Bold)> SplitKeywords(string texto, string[] keywords)
    {
        var ordered = keywords.OrderByDescending(k => k.Length).ToArray();
        var buffer = new System.Text.StringBuilder();
        var i = 0;
        while (i < texto.Length)
        {
            var match = ordered.FirstOrDefault(k =>
                i + k.Length <= texto.Length && string.CompareOrdinal(texto, i, k, 0, k.Length) == 0);
            if (match is not null)
            {
                if (buffer.Length > 0)
                {
                    yield return (buffer.ToString(), false);
                    buffer.Clear();
                }

                yield return (match, true);
                i += match.Length;
            }
            else
            {
                buffer.Append(texto[i]);
                i++;
            }
        }

        if (buffer.Length > 0)
            yield return (buffer.ToString(), false);
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

    /// <summary>Imagen de la firma del baúl resuelta para el rol del firmante, o <c>null</c>.</summary>
    private static byte[]? FirmaBaulDe(FurDocumentData data, string? rol) =>
        rol is not null
        && data.FirmaImagenes is not null
        && data.FirmaImagenes.TryGetValue(rol, out var imagen)
        && imagen.Length > 0
            ? imagen
            : null;

    /// <summary>Sello de validación biométrica del rol, solo si la identidad está validada.</summary>
    private static string? SelloIdentidadDe(FurDocumentData data, string? rol) =>
        rol is not null
        && data.IdentidadValidada
        && data.SellosIdentidad is not null
        && data.SellosIdentidad.TryGetValue(rol, out var sello)
        && !string.IsNullOrWhiteSpace(sello)
            ? sello
            : null;

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
