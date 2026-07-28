using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Tramites.Catalog;
using QuestPDF;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Generador del <b>Contrato Privado de Mandato</b> (ADR-0036, HU #10915). Porta las plantillas legacy
/// <c>mandated/*.hbs</c> de FLIT 1.0 a QuestPDF (tipo <c>mandato</c>), fusionable al Expediente
/// Consolidado (mismo patrón que <see cref="SolicitudVirtualPdfGenerator"/>). La variante la decide
/// <see cref="MandatoTemplateResolver"/> por el <c>template_code</c> del OT: <b>genérica</b> (mandatario
/// persona, ambos firman), <b>Sabaneta</b> (mandatario institucional UT-SETSA, solo firma el mandante) y
/// <b>Bello</b> (mandatario persona, representante legal de la UT-MAB). Dentro de cada variante, el texto
/// del MANDANTE cambia según sea persona natural (a nombre propio) o jurídica (su representante legal).
/// <para><b>Texto legal transcrito literal</b> de las plantillas legacy y marcado para revisión del PO
/// (ADR-0036 §10.2): esta implementación NO reinterpreta las cláusulas.</para>
/// Las firmas solo se pintan en estado distinto de borrador (<see cref="FurDocumentData.FirmasVisibles"/>).
/// </summary>
public sealed class MandatoPdfGenerator : IMandatoGenerator
{
    // Mandatario institucional por defecto cuando la config del OT no lo trae (fallback literal legacy).
    private const string SetsaNombre = "UNION TEMPORAL SERVICIOS ESPECIALIZADOS DE TRANSITO Y TRANSPORTE DE SABANETA SETSA";
    private const string SetsaNit = "900273813-7";
    private const string MabNombre = "UNION TEMPORAL MOVILIDAD AVANZADA DE BELLO MAB";
    private const string MabNit = "901783814-6";

    static MandatoPdfGenerator()
    {
        Settings.License = LicenseType.Community;
    }

    public GeneratedDocument GenerateMandato(MandatoData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var tramite = data.Tramite;
        var parte = tramite.Radicador;
        var esJuridica = parte?.EsJuridica ?? false;
        var variante = MandatoTemplateResolver.Resolve(data.TemplateCode);

        var esTraspaso = string.Equals(
            tramite.TipologiaCodigo, TramiteTipologiaCatalog.CodigoTraspasoStandard, StringComparison.OrdinalIgnoreCase);
        var nombreTramite = esTraspaso ? "TRASPASO DE PROPIEDAD" : "MATRÍCULA INICIAL";

        var placa = Val(tramite.Placa, "___");
        var ot = Val(tramite.Organismo.Nombre, "___");
        var ciudad = Val(tramite.Organismo.Ciudad, "___");
        var fecha = FormatFechaEs(tramite.FechaTramite ?? DateTime.UtcNow.AddHours(-5));

        var parrafos = BuildParrafos(data, parte, esJuridica, variante, nombreTramite, placa, ot, ciudad, fecha);

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
                    col.Item().AlignCenter().Text(t => t.Span("Contrato Privado de Mandato").Bold().FontSize(14));

                    foreach (var p in parrafos)
                        col.Item().PaddingTop(4).Text(p);

                    if (tramite.FirmasVisibles)
                        RenderFirmas(col, data, parte, esJuridica, variante);
                });
            });
        }).GeneratePdf();

        return new GeneratedDocument(
            "mandato",
            $"mandato_{SafeRef(tramite.ReferenceNumber)}.pdf",
            "application/pdf",
            bytes);
    }

    private static List<string> BuildParrafos(
        MandatoData data, DocumentParte? parte, bool esJuridica, MandatoVariante variante,
        string nombreTramite, string placa, string ot, string ciudad, string fecha) => variante switch
        {
            MandatoVariante.Sabaneta => Sabaneta(data, parte, esJuridica, nombreTramite, placa, ciudad, fecha),
            MandatoVariante.Bello => Bello(data, parte, esJuridica, nombreTramite, placa, ot, ciudad, fecha),
            _ => Generico(data, parte, esJuridica, nombreTramite, placa, ot, ciudad, fecha),
        };

    // ---- Genérica: mandatario persona (firmante del OT). Ambos firman. ----
    private static List<string> Generico(
        MandatoData data, DocumentParte? parte, bool esJuridica,
        string nombreTramite, string placa, string ot, string ciudad, string fecha)
    {
        var mandatario = MandatarioTexto(data.Mandatario);
        var intro = esJuridica
            ? $"Yo, {RlNombre(parte)}, mayor de edad, identificado con {RlTipo(parte)} No. {RlDoc(parte)}, " +
              $"en representación legal de {Empresa(parte)}, con NIT No. {Nit(parte)}, según lo acredita la " +
              "escritura pública y/o el certificado de existencia y representación expedido por la Cámara de " +
              "Comercio de Medellín y quien para los efectos del presente contrato se denominará EL MANDANTE. " +
              $"Y de {mandatario.Nombre} identificado con la cédula de ciudadanía No {mandatario.Documento}, " +
              "quien para los efectos del presente contrato se denominará EL MANDATARIO, hemos acordado suscribir " +
              ResolucionesCc()
            : $"Yo, {PnNombre(parte)}, mayor de edad, identificado con {PnTipo(parte)} número No {PnDoc(parte)}, " +
              "quien para los efectos del presente contrato se denominará EL MANDANTE. " +
              $"Y de {mandatario.Nombre} identificado con la cédula de ciudadanía No {mandatario.Documento}, " +
              "quien para los efectos del presente contrato se denominará EL MANDATARIO, hemos acordado suscribir " +
              ResolucionesCc();

        return
        [
            intro,
            PrimeraObjeto(nombreTramite),
            $"Identificado con placas {placa}. Ante el organismo de tránsito de {ot}.",
            "Como consecuencia, EL MANDATARIO queda facultado para realizar todas las gestiones propias de este " +
            "mandato y en especial para representar, notificarse, recibir, impugnar, desistir, sustituir, reasumir, " +
            "pedir, conciliar o asumir obligaciones en nombre del MANDANTE.",
            SegundaObligaciones(),
            $"Dicho contrato se firmó entre las partes el {fecha} en la ciudad de {ciudad}.",
        ];
    }

    // ---- Sabaneta: mandatario institucional UT-SETSA. Solo firma el MANDANTE. ----
    private static List<string> Sabaneta(
        MandatoData data, DocumentParte? parte, bool esJuridica,
        string nombreTramite, string placa, string ciudad, string fecha)
    {
        var inst = Val(data.InstitutionalMandataryName, SetsaNombre);
        var nit = Val(data.InstitutionalMandataryNit, SetsaNit);
        var intro = esJuridica
            ? $"Yo, {RlNombre(parte)}, mayor de edad, identificado con {RlTipo(parte)} número No {RlDoc(parte)}, " +
              $"en representación legal de {Empresa(parte)}, con NIT No. {Nit(parte)}, según lo acredita la " +
              "escritura pública y/o el certificado de existencia y representación expedido por la Cámara de " +
              "Comercio de Medellín y quien para los efectos del presente contrato se denominará EL MANDANTE. " +
              $"Y de {inst}, con NIT N° {nit}, quien para efectos del presente contrato se denominará EL MANDATARIO, " +
              "hemos acordado suscribir el siguiente contrato de mandato mediante el cual el mandatario se hace " +
              $"cargo de la gestión de realizar el trámite de {nombreTramite} del vehículo de placas: {placa}, " +
              "por cuenta y riesgo del mandante."
            : $"Yo, {PnNombre(parte)}, mayor de edad, identificado con {PnTipo(parte)} número No {PnDoc(parte)}, " +
              "en representación propia y quien para los efectos del presente contrato se denominará EL MANDANTE. " +
              $"Y de {inst}, con NIT N° {nit}, quien para efectos del presente contrato se denominará EL MANDATARIO, " +
              "hemos acordado suscribir el siguiente contrato de mandato mediante el cual el mandatario se hace " +
              $"cargo de la gestión de realizar el trámite de {nombreTramite} del vehículo de placas: {placa}, " +
              "por cuenta y riesgo del mandante.";

        return
        [
            intro,
            "OBLIGACIONES DEL MANDANTE: EL MANDANTE declara que la información contenida en los documentos que se " +
            "anexan a la solicitud del trámite es veraz y auténtica, razón por la que se hace responsable ante las " +
            "autoridades competentes de cualquier irregularidad que los mismos puedan contener; al igual dejando " +
            "indemne a la UT-SETSA de cualquier responsabilidad en los que se ve comprometido la confidencialidad y " +
            "divulgación de la información legalmente protegida mediante los parámetros y disposiciones de la ley " +
            "1581 del 2012 y demás normas que se dicten en la materia.",
            $"Dicho contrato se firmó entre las partes el {fecha} en la ciudad de {ciudad}.",
        ];
    }

    // ---- Bello: mandatario persona, representante legal de la UT-MAB. Ambos firman. ----
    private static List<string> Bello(
        MandatoData data, DocumentParte? parte, bool esJuridica,
        string nombreTramite, string placa, string ot, string ciudad, string fecha)
    {
        var inst = Val(data.InstitutionalMandataryName, MabNombre);
        var nit = Val(data.InstitutionalMandataryNit, MabNit);
        var mandatario = MandatarioTexto(data.Mandatario);
        var intro = esJuridica
            ? $"Yo, {RlNombre(parte)}, mayor de edad, identificado con {RlTipo(parte)} número No {RlDoc(parte)}, " +
              $"en representación legal de {Empresa(parte)}, con NIT No. {Nit(parte)}, según lo acredita la " +
              "escritura pública y/o el Certificado de Existencia y Representación expedido por la Cámara de " +
              "Comercio de Medellín y quien para los efectos del presente contrato se denominará EL MANDANTE. " +
              $"Y de la otra parte, {mandatario.Nombre} identificado con la cédula de ciudadanía No {mandatario.Documento}, " +
              $"Representante Legal de {inst}, con NIT No. {nit}, quien para efectos del presente contrato se " +
              "denominará EL MANDATARIO, hemos acordado suscribir el siguiente contrato de mandato mediante el cual " +
              "el mandatario se hace cargo de la gestión de realizar el trámite según las siguientes cláusulas."
            : $"Yo, {PnNombre(parte)}, mayor de edad, identificado con {PnTipo(parte)} número No {PnDoc(parte)}, " +
              "quien para los efectos del presente contrato se denominará EL MANDANTE. " +
              $"Y de la otra parte, {mandatario.Nombre} identificado con la cédula de ciudadanía No {mandatario.Documento}, " +
              $"Representante Legal de {inst}, con NIT No. {nit}, quien para efectos del presente contrato se " +
              "denominará EL MANDATARIO, hemos acordado suscribir el siguiente contrato de mandato mediante el cual " +
              "el mandatario se hace cargo de la gestión de realizar el trámite según las siguientes cláusulas.";

        return
        [
            intro,
            PrimeraObjeto(nombreTramite),
            $"Identificado con placas {placa}. Ante el organismo de tránsito de {ot}.",
            "OBLIGACIONES DEL MANDANTE: EL MANDANTE declara que la información contenida en los documentos que se " +
            "anexan a la solicitud del trámite es veraz y auténtica, razón por la que se hace responsable ante las " +
            "autoridades competentes de cualquier irregularidad que los mismos puedan contener; de igual modo, " +
            $"declara que deja indemne a la {inst} de cualquier responsabilidad civil o penal, asimismo, EL MANDANTE " +
            "de forma expresa asevera que deja impoluto a EL MANDATARIO en todos los casos que se vea comprometido " +
            "la confidencialidad y divulgación de la información legalmente protegida mediante los parámetros y " +
            "disposiciones de la ley 1581 del 2012 y demás normas que se dicten en la materia.",
            $"Dicho contrato se firmó entre las partes el {fecha} en el municipio de Bello, Antioquia.",
        ];
    }

    private static string ResolucionesCc() =>
        "el siguiente contrato de mandato cumpliendo con la Resolución 12379 expedida por el Ministerio de " +
        "Transporte el 28 de diciembre de 2012 (Art. 5), así como la Resolución 20233040017145 de 2023 \"por la " +
        "cual se modifica la Resolución 20223040045295 de 2022 y se dictan disposiciones para la correcta y amplia " +
        "implementación de la política de simplificación y racionalización de trámites\", que se regirá por las " +
        "normas civiles y comerciales que regulan la materia en concordancia con el Art. 2149 C.C. según las " +
        "siguientes cláusulas.";

    private static string PrimeraObjeto(string nombreTramite) =>
        "PRIMERA: OBJETO DEL MANDATO. EL MANDANTE confiere a EL MANDATARIO poder amplio y suficiente para que en su " +
        $"nombre y representación adelante, radique y reclame ante el organismo de tránsito el trámite de {nombreTramite} " +
        "respecto del automotor.";

    private static string SegundaObligaciones() =>
        "SEGUNDA: OBLIGACIONES DEL MANDANTE. EL MANDANTE declara que la información contenida en los documentos que " +
        "se anexan a la solicitud del trámite es veraz y auténtica, razón por la que se hace responsable ante la " +
        "autoridad competente de cualquier irregularidad que los mismos puedan contener.";

    private static void RenderFirmas(
        ColumnDescriptor col, MandatoData data, DocumentParte? parte, bool esJuridica, MandatoVariante variante)
    {
        var tramite = data.Tramite;

        // Sabaneta: mandatario institucional ⇒ solo firma el MANDANTE (+ bloque de identificación).
        if (variante == MandatoVariante.Sabaneta)
        {
            col.Item().PaddingTop(40).Column(sig =>
            {
                sig.Item().Text(t => t.Span("MANDANTE").Bold());
                RenderFirmaSlot(sig, tramite, parte?.Rol, "_______________________________");
                foreach (var line in MandanteIdentificacion(parte, esJuridica))
                    sig.Item().Text(t => t.Span(line).FontSize(10));
                RenderSello(sig, tramite, parte?.Rol);
            });
            return;
        }

        // Genérica / Bello: firman MANDANTE y MANDATARIO.
        var mandatario = MandatarioTexto(data.Mandatario);
        col.Item().PaddingTop(40).Row(row =>
        {
            row.RelativeItem().Column(sig =>
            {
                sig.Item().Text(t => t.Span("MANDANTE").Bold());
                RenderFirmaSlot(sig, tramite, parte?.Rol, "____________________________");
                foreach (var line in MandanteIdentificacion(parte, esJuridica))
                    sig.Item().Text(t => t.Span(line).FontSize(10));
                RenderSello(sig, tramite, parte?.Rol);
            });
            row.RelativeItem().Column(sig =>
            {
                sig.Item().Text(t => t.Span("MANDATARIO").Bold());
                sig.Item().PaddingTop(28).Text("____________________________");
                sig.Item().Text(t => t.Span(mandatario.Nombre).FontSize(10));
                sig.Item().Text(t => t.Span($"C.C. {mandatario.Documento}").FontSize(10));
            });
        });
    }

    // HU #10997 — pinta la firma del MANDANTE según el mecanismo aplicable: imagen del baúl de firmas si
    // el trámite la resolvió para el rol (persona jurídica ⇒ representante legal), o la línea en blanco para
    // firma manuscrita en su ausencia. La llave del diccionario es el rol de la parte radicadora.
    private static void RenderFirmaSlot(ColumnDescriptor sig, FurDocumentData tramite, string? rol, string underline)
    {
        if (rol is not null
            && tramite.FirmaImagenes is not null
            && tramite.FirmaImagenes.TryGetValue(rol, out var imagen)
            && imagen.Length > 0)
        {
            sig.Item().PaddingTop(4).Height(32).Image(imagen).FitHeight();
        }
        else
        {
            sig.Item().PaddingTop(28).Text(underline);
        }
    }

    // HU #10997 — sello de validación biométrica de identidad bajo la firma, solo si la identidad está
    // validada y hay sello para el rol (mismo patrón que la compraventa autogenerada).
    private static void RenderSello(ColumnDescriptor sig, FurDocumentData tramite, string? rol)
    {
        if (rol is not null
            && tramite.IdentidadValidada
            && tramite.SellosIdentidad is not null
            && tramite.SellosIdentidad.TryGetValue(rol, out var sello)
            && !string.IsNullOrWhiteSpace(sello))
        {
            sig.Item().PaddingTop(2).Text(t => t.Span(sello).FontSize(6.5f).FontColor(Colors.Grey.Darken2));
        }
    }

    private static IEnumerable<string> MandanteIdentificacion(DocumentParte? parte, bool esJuridica)
    {
        if (esJuridica)
        {
            yield return $"NOMBRE: {RlNombre(parte)}";
            yield return $"{MapDoc(parte?.RepresentanteLegalTipoDoc).ToUpperInvariant()}: {RlDoc(parte)}";
            yield return $"EMPRESA: {Empresa(parte)}";
            yield return $"NIT: {Nit(parte)}";
        }
        else
        {
            yield return $"NOMBRE: {PnNombre(parte)}";
            yield return $"{MapDoc(parte?.DocumentType).ToUpperInvariant()}: {PnDoc(parte)}";
        }
    }

    private static (string Nombre, string Documento) MandatarioTexto(MandatarioFirmante? m) =>
        (Val(m?.Nombre, "___"), Val(m?.Documento, "___"));

    // ---- Accessores del MANDANTE (persona natural / jurídica + su representante legal) ----
    private static string PnNombre(DocumentParte? p) => Val(p?.Nombre, "___");
    private static string PnTipo(DocumentParte? p) => MapDoc(p?.DocumentType);
    private static string PnDoc(DocumentParte? p) => Val(p?.Documento, "___");
    private static string Empresa(DocumentParte? p) => Val(p?.Nombre, "___");
    private static string Nit(DocumentParte? p) => Val(p?.Documento, "___");
    private static string RlNombre(DocumentParte? p) => Val(p?.RepresentanteLegalNombre, "___");
    private static string RlTipo(DocumentParte? p) => MapDoc(p?.RepresentanteLegalTipoDoc);
    private static string RlDoc(DocumentParte? p) => Val(p?.RepresentanteLegalDocumento, "___");

    // Meses en español SIN depender de una cultura instalada (el runtime puede correr en
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
