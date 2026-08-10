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
/// Generador del <b>Contrato Privado de Mandato</b> (ADR-0036, HU #10915). Porta las plantillas legacy
/// <c>mandated/*.hbs</c> de FLIT 1.0 a QuestPDF (tipo <c>mandato</c>), fusionable al Expediente
/// Consolidado (mismo patrón que <see cref="SolicitudVirtualPdfGenerator"/>). La variante la decide
/// <see cref="MandatoTemplateResolver"/> por el <c>template_code</c> del OT: <b>genérica</b> (mandatario
/// persona, ambos firman electrónicamente), <b>Sabaneta</b> (mandatario institucional UT-SETSA),
/// <b>Bello</b> (mandatario persona, representante legal de la UT-MAB) y <b>municipio</b> (redacción
/// corta PN/PJ de Envigado/Funza/Medellín). Dentro de cada variante, el texto del MANDANTE cambia según
/// sea persona natural (a nombre propio) o jurídica (su representante legal).
/// <para><b>HU #11205 — quién firma electrónicamente.</b> Cuando el OT tiene una EMPRESA RELACIONADA como
/// mandatario (familia <c>organismo_transito</c>), su firma es MANUAL: no se plasma su validación de
/// identidad, solo queda la línea sobre la razón social y el NIT. Aplica a Sabaneta <b>y a Bello</b>: los
/// <c>.md</c> de ambos traen un bloque de firma electrónica del mandatario, pero se consideran
/// desactualizados en ese punto (D4) y se usan como fuente del texto legal, no de la política de firma.
/// El MANDANTE sigue firmando con su validación de identidad en todos los casos.</para>
/// <para><b>Texto legal transcrito literal</b> de las plantillas legacy y marcado para revisión del PO
/// (ADR-0036 §10.2): esta implementación NO reinterpreta las cláusulas.</para>
/// <para><b>Las firmas se pintan en todos los estados.</b> Hasta ahora se ocultaban en borrador y
/// subsanación (punto 18 del requerimiento de ADR-0036), lo que dejaba el contrato sin siquiera la
/// línea para firmar a mano justo cuando el gestor lo revisa; y obligaba a regenerar el expediente
/// completo al entregar para que el organismo no lo recibiera en blanco. El recuadro se pinta
/// siempre: lo que no exista todavía sale como línea vacía, que es lo que un contrato en borrador
/// debe mostrar.</para>
/// </summary>
public sealed class MandatoPdfGenerator : IMandatoGenerator
{
    // Mandatario institucional por defecto cuando la config del OT no lo trae (fallback literal legacy).
    private const string SetsaNombre = "UNION TEMPORAL SERVICIOS ESPECIALIZADOS DE TRANSITO Y TRANSPORTE DE SABANETA SETSA";
    private const string SetsaNit = "900273813-7";
    private const string MabNombre = "UNION TEMPORAL MOVILIDAD AVANZADA DE BELLO MAB";
    private const string MabNit = "901783814-6";

    // HU #11204 — datos del OT que hasta ahora estaban incrustados en el texto. Ahora vienen de la
    // configuración del organismo; estas constantes quedan como fallback literal, de modo que un OT sin
    // configurarlos emite exactamente el mismo contrato que antes (AC5).
    private const string CamaraPorDefecto = "Medellín";
    private const string SetsaSigla = "UT-SETSA";

    /// <summary>Ciudad de la Cámara de Comercio que acredita al MANDANTE (config del OT, HU #11204).</summary>
    private static string Camara(MandatoData data) => Val(data.ChamberCity, CamaraPorDefecto);

    /// <summary>Sigla de la unión temporal para la cláusula de obligaciones (config del OT, HU #11204).</summary>
    private static string Sigla(MandatoData data, string fallback) => Val(data.MandatarySigla, fallback);

    static MandatoPdfGenerator()
    {
        Settings.License = LicenseType.Community;
    }

    public GeneratedDocument GenerateMandato(MandatoData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var kind = MandatoCustomTemplateKindCodes.Resolve(data.CustomTemplateKind);
        if (kind == MandatoCustomTemplateKindCodes.Editor
            && !string.IsNullOrWhiteSpace(data.CustomTemplateBody))
        {
            return GenerateFromEditor(data);
        }

        if (kind == MandatoCustomTemplateKindCodes.Pdf
            && data.CustomTemplatePdf is { Length: > 0 })
        {
            return GenerateFromCustomPdf(data);
        }

        return GenerateFromSystemTemplate(data);
    }

    private static GeneratedDocument GenerateFromSystemTemplate(MandatoData data)
    {
        var tramite = data.Tramite;
        // HU #11030 — el mandato lo otorga quien VENDE (en matrícula, el radicador).
        var parte = tramite.Mandante;
        var esJuridica = parte?.EsJuridica ?? false;
        var variante = MandatoTemplateResolver.Resolve(data.TemplateCode);

        var esTraspaso = string.Equals(
            tramite.TipologiaCodigo, TramiteTipologiaCatalog.CodigoTraspasoStandard, StringComparison.OrdinalIgnoreCase);
        // HU #11206 — el objeto del contrato incluye las transformaciones del trámite. Se compone aquí,
        // una sola vez, para que todas las familias de plantilla lo redacten idéntico (AC4). Sin
        // transformaciones queda exactamente el texto de siempre (AC3).
        var nombreTramite = MandatoObjetoComposer.Componer(
            esTraspaso ? "TRASPASO DE PROPIEDAD" : "MATRÍCULA INICIAL",
            data.Transformaciones);

        var placa = Val(tramite.Placa, "___");
        var ot = Val(tramite.Organismo.Nombre, "___");
        // HU #11016 — la ciudad puede no venir (el field_value trae el código DIVIPOLA, que se descarta):
        // en ese caso la cláusula de cierre no menciona ciudad en vez de imprimir un código o «___».
        var ciudad = tramite.Organismo.Ciudad?.Trim() ?? string.Empty;
        var fecha = FormatFechaEs(tramite.FechaTramite ?? DateTime.UtcNow.AddHours(-5));

        var parrafos = BuildParrafos(data, parte, esJuridica, variante, nombreTramite, placa, ot, ciudad, fecha);

        var bytes = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                // HU #11033 — membrete institucional FLIT (HU #10856), igual que los certificados
                // generados: bandas arriba y abajo, contenido dentro del margen FLIT.
                FlitLetterhead.ApplyTo(page);
                // HU #11034 — cuerpo 9pt y espaciado corto: con 11pt el contrato se pasaba a una segunda
                // hoja y las firmas quedaban solas. El texto legal es largo y no se puede recortar, así
                // que lo que se ajusta es la caja tipográfica.
                page.DefaultTextStyle(t => t.FontSize(9).FontFamily(FlitDocumentTheme.FontRegular));

                FlitLetterhead.Content(page).Column(col =>
                {
                    col.Spacing(3);
                    col.Item().AlignCenter().Text(t => t.Span("Contrato Privado de Mandato").Bold().FontSize(12));

                    foreach (var p in parrafos)
                        RenderParrafo(col, p);

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

    /// <summary>Plantilla editor: cuerpo con placeholders + pie de firmas FLIT fijo abajo.</summary>
    private static GeneratedDocument GenerateFromEditor(MandatoData data)
    {
        var tramite = data.Tramite;
        var parte = tramite.Mandante;
        var esJuridica = parte?.EsJuridica ?? false;
        var body = ApplyPlaceholders(data.CustomTemplateBody!, data);

        var bytes = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                FlitLetterhead.ApplyTo(page);
                page.DefaultTextStyle(t => t.FontSize(9).FontFamily(FlitDocumentTheme.FontRegular));
                FlitLetterhead.Content(page).Column(col =>
                {
                    col.Spacing(3);
                    col.Item().AlignCenter().Text(t => t.Span("Contrato Privado de Mandato").Bold().FontSize(12));
                    foreach (var line in body.Split('\n'))
                        col.Item().Text(line.TrimEnd()).FontSize(9);
                    RenderFirmas(col, data, parte, esJuridica, MandatoVariante.Generico);
                });
            });
        }).GeneratePdf();

        return new GeneratedDocument(
            "mandato",
            $"mandato_{SafeRef(tramite.ReferenceNumber)}.pdf",
            "application/pdf",
            bytes);
    }

    /// <summary>
    /// PDF propio del OT como cuerpo + página de firmas FLIT al final (mismo layout abajo).
    /// </summary>
    private static GeneratedDocument GenerateFromCustomPdf(MandatoData data)
    {
        var tramite = data.Tramite;
        var parte = tramite.Mandante;
        var esJuridica = parte?.EsJuridica ?? false;

        var firmasPage = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                FlitLetterhead.ApplyTo(page);
                page.DefaultTextStyle(t => t.FontSize(9).FontFamily(FlitDocumentTheme.FontRegular));
                FlitLetterhead.Content(page).Column(col =>
                {
                    col.Item().AlignCenter().Text(t =>
                        t.Span("Firmas del Contrato Privado de Mandato").Bold().FontSize(11));
                    RenderFirmas(col, data, parte, esJuridica, MandatoVariante.Generico);
                });
            });
        }).GeneratePdf();

        var merged = MergePdfs(data.CustomTemplatePdf!, firmasPage);
        return new GeneratedDocument(
            "mandato",
            $"mandato_{SafeRef(tramite.ReferenceNumber)}.pdf",
            "application/pdf",
            merged);
    }

    private static byte[] MergePdfs(byte[] first, byte[] second)
    {
        using var output = new PdfSharpCore.Pdf.PdfDocument();
        using (var ms1 = new MemoryStream(first))
        using (var src1 = PdfSharpCore.Pdf.IO.PdfReader.Open(ms1, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Import))
        {
            for (var i = 0; i < src1.PageCount; i++)
                output.AddPage(src1.Pages[i]);
        }

        using (var ms2 = new MemoryStream(second))
        using (var src2 = PdfSharpCore.Pdf.IO.PdfReader.Open(ms2, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Import))
        {
            for (var i = 0; i < src2.PageCount; i++)
                output.AddPage(src2.Pages[i]);
        }

        using var outMs = new MemoryStream();
        output.Save(outMs, false);
        return outMs.ToArray();
    }

    private static string ApplyPlaceholders(string body, MandatoData data)
    {
        var tramite = data.Tramite;
        var parte = tramite.Mandante;
        var esTraspaso = string.Equals(
            tramite.TipologiaCodigo, TramiteTipologiaCatalog.CodigoTraspasoStandard, StringComparison.OrdinalIgnoreCase);
        var nombreTramite = MandatoObjetoComposer.Componer(
            esTraspaso ? "TRASPASO DE PROPIEDAD" : "MATRÍCULA INICIAL",
            data.Transformaciones);
        var (mandNombre, mandDoc) = MandatarioTexto(data.Mandatario);

        return body
            .Replace("{{placa}}", Val(tramite.Placa, "___"), StringComparison.OrdinalIgnoreCase)
            .Replace("{{tramite}}", nombreTramite, StringComparison.OrdinalIgnoreCase)
            .Replace("{{organismo}}", Val(tramite.Organismo.Nombre, "___"), StringComparison.OrdinalIgnoreCase)
            .Replace("{{ciudad}}", tramite.Organismo.Ciudad?.Trim() ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace(
                "{{fecha}}",
                FormatFechaEs(tramite.FechaTramite ?? DateTime.UtcNow.AddHours(-5)),
                StringComparison.OrdinalIgnoreCase)
            .Replace("{{mandante_nombre}}", Val(parte?.Nombre, "___"), StringComparison.OrdinalIgnoreCase)
            .Replace("{{mandante_documento}}", Val(parte?.Documento, "___"), StringComparison.OrdinalIgnoreCase)
            .Replace("{{mandatario_nombre}}", mandNombre, StringComparison.OrdinalIgnoreCase)
            .Replace("{{mandatario_documento}}", mandDoc, StringComparison.OrdinalIgnoreCase)
            .Replace(
                "{{mandatario_institucional}}",
                Val(data.InstitutionalMandataryName, "___"),
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "{{mandatario_nit}}",
                Val(data.InstitutionalMandataryNit, "___"),
                StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> BuildParrafos(
        MandatoData data, DocumentParte? parte, bool esJuridica, MandatoVariante variante,
        string nombreTramite, string placa, string ot, string ciudad, string fecha) => variante switch
        {
            MandatoVariante.Sabaneta => Sabaneta(data, parte, esJuridica, nombreTramite, placa, ciudad, fecha),
            MandatoVariante.Bello => Bello(data, parte, esJuridica, nombreTramite, placa, ot, ciudad, fecha),
            MandatoVariante.Municipio => Municipio(data, parte, esJuridica, nombreTramite, placa, ciudad, fecha),
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
              $"Comercio de {Camara(data)} y quien para los efectos del presente contrato se denominará EL MANDANTE. " +
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
            CierreFirma(fecha, ciudad),
        ];
    }

    // ---- Sabaneta: mandatario institucional UT-SETSA. Solo firma el MANDANTE. ----
    // Tipo Abierto (Manual + sin firmante): el cuerpo usa la redacción genérica con placeholders,
    // para no rellenar la UT cuando el negocio pide líneas abiertas.
    private static List<string> Sabaneta(
        MandatoData data, DocumentParte? parte, bool esJuridica,
        string nombreTramite, string placa, string ciudad, string fecha)
    {
        if (data.ModoFirmaMandatario == MandatarioFirmaModo.Manual && data.Mandatario is null)
        {
            var ot = Val(data.Tramite.Organismo.Nombre, "___");
            return Generico(data, parte, esJuridica, nombreTramite, placa, ot, ciudad, fecha);
        }

        var inst = Val(data.InstitutionalMandataryName, SetsaNombre);
        var nit = Val(data.InstitutionalMandataryNit, SetsaNit);
        var intro = esJuridica
            ? $"Yo, {RlNombre(parte)}, mayor de edad, identificado con {RlTipo(parte)} número No {RlDoc(parte)}, " +
              $"en representación legal de {Empresa(parte)}, con NIT No. {Nit(parte)}, según lo acredita la " +
              "escritura pública y/o el certificado de existencia y representación expedido por la Cámara de " +
              $"Comercio de {Camara(data)} y quien para los efectos del presente contrato se denominará EL MANDANTE. " +
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
            $"indemne a la {Sigla(data, SetsaSigla)} de cualquier responsabilidad en los que se ve comprometido la confidencialidad y " +
            "divulgación de la información legalmente protegida mediante los parámetros y disposiciones de la ley " +
            "1581 del 2012 y demás normas que se dicten en la materia.",
            CierreFirma(fecha, ciudad),
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
              $"Comercio de {Camara(data)} y quien para los efectos del presente contrato se denominará EL MANDANTE. " +
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

    // ---- Municipio (Envigado / Funza / Medellín): redacción corta PN; PJ con RL. Ambos firman. ----
    private static List<string> Municipio(
        MandatoData data, DocumentParte? parte, bool esJuridica,
        string nombreTramite, string placa, string ciudad, string fecha)
    {
        var mandatario = MandatarioTexto(data.Mandatario);
        var intro = esJuridica
            ? $"Yo, {RlNombre(parte)}, mayor de edad, identificado con {RlTipo(parte)} número No {RlDoc(parte)}, " +
              $"en representación legal de {Empresa(parte)}, con NIT No. {Nit(parte)}, quien para los efectos " +
              "del presente contrato se denominará EL MANDANTE. " +
              $"Y de {mandatario.Nombre} identificado con la cédula de ciudadanía No {mandatario.Documento}, " +
              "quien para los efectos del presente contrato se denominará EL MANDATARIO, hemos acordado " +
              "suscribir el siguiente contrato de mandato mediante el cual el mandatario se hace cargo de la " +
              $"radicación y reclamación del trámite de {nombreTramite} vehículo de placa: {placa}, por " +
              "cuenta y riesgo del mandante."
            : $"Yo, {PnNombre(parte)}, mayor de edad, identificado con {PnTipo(parte)} número No {PnDoc(parte)}, " +
              "quien para los efectos del presente contrato se denominará EL MANDANTE. " +
              $"Y de {mandatario.Nombre} identificado con la cédula de ciudadanía No {mandatario.Documento}, " +
              "quien para los efectos del presente contrato se denominará EL MANDATARIO, hemos acordado " +
              "suscribir el siguiente contrato de mandato mediante el cual el mandatario se hace cargo de la " +
              $"radicación y reclamación del trámite de {nombreTramite} vehículo de placa: {placa}, por " +
              "cuenta y riesgo del mandante.";

        return
        [
            intro,
            "OBLIGACIONES DEL MANDANTE: EL MANDANTE declara que la información contenida en los documentos que se " +
            "anexan a la solicitud del trámite es veraz y auténtica, razón por la que se hace responsable ante la " +
            "autoridad competente de cualquier irregularidad que los mismos puedan contener.",
            CierreFirma(fecha, ciudad),
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

    /// <summary>Cláusula de cierre: menciona la ciudad solo si se conoce (HU #11016).</summary>
    private static string CierreFirma(string fecha, string ciudad) =>
        string.IsNullOrEmpty(ciudad)
            ? $"Dicho contrato se firmó entre las partes el {fecha}."
            : $"Dicho contrato se firmó entre las partes el {fecha} en la ciudad de {ciudad}.";

    private static void RenderFirmas(
        ColumnDescriptor col, MandatoData data, DocumentParte? parte, bool esJuridica, MandatoVariante variante)
    {
        var tramite = data.Tramite;

        var modo = ModoFirmaMandatario(data);

        // HU #11034 — separación reducida para que las firmas quepan en la misma hoja que el cuerpo.
        col.Item().PaddingTop(16).Row(row =>
        {
            row.RelativeItem().Column(sig =>
            {
                sig.Item().Text(t => t.Span("MANDANTE").Bold());
                RenderMandanteFirma(sig, tramite, parte, esJuridica);
            });

            // Con convenio entre la compañía y el organismo, el recuadro solo lleva al MANDANTE: el
            // mandatario no firma. Sus datos siguen nombrados en el CUERPO del contrato, así que el
            // documento no pierde al actor, solo su espacio de firma.
            if (modo == MandatarioFirmaModo.SinBloque)
                return;

            var manual = modo == MandatarioFirmaModo.Manual;
            row.RelativeItem().Column(sig =>
            {
                sig.Item().Text(t => t.Span("MANDATARIO").Bold());
                // HU #11030 — la firma del mandatario no se pintaba nunca: siempre salía la línea vacía
                // aunque tuviera firma en el baúl o identidad validada. Misma precedencia que el mandante.
                // HU #11046 — la estampa va SOBRE la línea, igual que el mandante.
                // HU #11170 — la firma del baúl del mandatario también lleva su vigencia y su hash.
                FlitFirmaBlock.Render(
                    sig,
                    manual ? null : data.Mandatario?.FirmaImagen,
                    manual ? null : data.Mandatario?.SelloIdentidad,
                    MandatarioIdentificacion(data.Mandatario, data),
                    FlitFirmaLinea.Underscores,
                    selloBaul: manual ? null : SelloBaulDe(data.Mandatario));
            });
        });
    }

    /// <summary>
    /// Cómo aparece el MANDATARIO en el recuadro de firmas. Orden deliberado:
    /// <list type="number">
    ///   <item><b>Modo resuelto por el caller</b> (<see cref="MandatoData.ModoFirmaMandatario"/>):
    ///   institucional/convenio → <c>SinBloque</c>; abierto / firma física → <c>Manual</c> (líneas);
    ///   persona/RL → <c>Estampada</c>. El caller gana para no pisar el tipo Abierto.</item>
    ///   <item><b>Sabaneta sin modo explícito Manual/Estampada:</b> sin bloque — su mandatario es la UT,
    ///   no una persona. Si el caller pidió <c>Manual</c> (p. ej. tipo Abierto sobre plantilla sabaneta),
    ///   se respeta el bloque con líneas.</item>
    ///   <item><b>Familia organismo</b> (p. ej. Bello): línea manual si el modo no vino ya resuelto.</item>
    /// </list>
    /// </summary>
    private static MandatarioFirmaModo ModoFirmaMandatario(MandatoData data)
    {
        // Caller explícito (Abierto=Manual, Institucional/convenio=SinBloque, Persona=Estampada/Manual).
        if (data.ModoFirmaMandatario == MandatarioFirmaModo.SinBloque)
            return MandatarioFirmaModo.SinBloque;
        if (data.ModoFirmaMandatario == MandatarioFirmaModo.Manual)
            return MandatarioFirmaModo.Manual;

        // Plantilla Sabaneta histórica: sin bloque salvo que el caller haya pedido Manual/Estampada.
        if (MandatoTemplateResolver.Resolve(data.TemplateCode) == MandatoVariante.Sabaneta)
            return MandatarioFirmaModo.SinBloque;

        if (MandatarioEsEmpresa(data))
            return MandatarioFirmaModo.Manual;

        return data.ModoFirmaMandatario;
    }

    /// <summary>
    /// HU #11205 (D4) — ¿el mandatario del OT es una empresa relacionada? La señal explícita es la
    /// familia (HU #11204); el nombre institucional se conserva como señal heredada para los OT que
    /// todavía no tengan familia configurada.
    ///
    /// <para>Manda la REGLA, no las plantillas: los <c>.md</c> de Bello y Sabaneta incluyen un bloque de
    /// firma electrónica para el mandatario, pero se consideran desactualizados en ese punto y se usan
    /// como fuente del texto legal, no de la política de firma.</para>
    /// </summary>
    private static bool MandatarioEsEmpresa(MandatoData data) =>
        data.Familia == MandatoFamilia.OrganismoTransito
        || !string.IsNullOrWhiteSpace(data.InstitutionalMandataryName);

    /// <summary>
    /// Bloque de firma del MANDANTE (HU #11046): estampa (baúl o sello de identidad) sobre la línea y,
    /// debajo, su identificación. La prioridad del baúl (HU #11031) la resuelve
    /// <see cref="FlitFirmaBlock"/>.
    /// </summary>
    private static void RenderMandanteFirma(
        ColumnDescriptor sig, FurDocumentData tramite, DocumentParte? parte, bool esJuridica)
    {
        FlitFirmaBlock.Render(
            sig,
            FirmaBaulDe(tramite, parte?.Rol),
            SelloIdentidadDe(tramite, parte?.Rol),
            MandanteIdentificacion(parte, esJuridica),
            FlitFirmaLinea.Underscores,
            // HU #11170 — vigencia y hash de la firma custodiada, como en el FUR.
            selloBaul: FlitFirmaBaulSello.Resolve(tramite.FirmaBaulMetadatos, parte?.Rol, incluirIdentificacion: false));
    }

    /// <summary>
    /// HU #11170 — trazabilidad de la firma del baúl del MANDATARIO. Su firma no se resuelve por rol del
    /// trámite (no es parte: es quien recibe el poder), así que los metadatos viajan en el propio
    /// <see cref="MandatarioFirmante"/>.
    /// </summary>
    private static string? SelloBaulDe(MandatarioFirmante? mandatario) =>
        mandatario?.FirmaBaulMetadatos is { } meta
            ? FlitFirmaBaulSello.Build(meta, incluirIdentificacion: false)
            : null;

    /// <summary>Imagen de la firma del baúl resuelta para el rol, o <c>null</c> si no tiene.</summary>
    private static byte[]? FirmaBaulDe(FurDocumentData tramite, string? rol) =>
        rol is not null
        && tramite.FirmaImagenes is not null
        && tramite.FirmaImagenes.TryGetValue(rol, out var imagen)
        && imagen.Length > 0
            ? imagen
            : null;

    /// <summary>Sello de validación biométrica del rol, solo si la identidad está validada.</summary>
    private static string? SelloIdentidadDe(FurDocumentData tramite, string? rol) =>
        rol is not null
        && tramite.IdentidadValidada
        && tramite.SellosIdentidad is not null
        && tramite.SellosIdentidad.TryGetValue(rol, out var sello)
        && !string.IsNullOrWhiteSpace(sello)
            ? sello
            : null;

    // HU #11046 — la composición del bloque de firma (estampa sobre la línea, datos debajo) y la
    // prioridad del baúl de la HU #11031 viven ahora en FlitFirmaBlock, compartido con la solicitud de
    // trámite virtual. Aquí solo se resuelven los datos de cada firmante.

    // HU #10998 — palabras clave del mandato que se resaltan en negrita dentro del cuerpo (las partes
    // definidas y los encabezados de cláusula). Se ordenan por longitud descendente al tokenizar para que
    // los encabezados compuestos ganen sobre sus subcadenas (p. ej. "SEGUNDA: ..." sobre "MANDANTE").
    private static readonly string[] MandatoKeywords =
    [
        "PRIMERA: OBJETO DEL MANDATO",
        "SEGUNDA: OBLIGACIONES DEL MANDANTE",
        "OBLIGACIONES DEL MANDANTE",
        "MANDATARIO",
        "MANDANTE",
        // Marcadores del preview de plataforma (MandatoPreviewSample): se muestran en negrita.
        MandatoPreviewSample.PhRlNombre,
        MandatoPreviewSample.PhRlDocumento,
        MandatoPreviewSample.PhRazonSocial,
        MandatoPreviewSample.PhNit,
        MandatoPreviewSample.PhPnNombre,
        MandatoPreviewSample.PhPnDocumento,
        MandatoPreviewSample.PhMandatarioNombre,
        MandatoPreviewSample.PhMandatarioDocumento,
        MandatoPreviewSample.PhPlaca,
        MandatoPreviewSample.PhCamara,
    ];

    // HU #11034 — párrafos JUSTIFICADOS y compactos: el contrato debe caber en una sola hoja, firmas
    // incluidas, y el texto justificado es lo que espera un documento legal.
    private static void RenderParrafo(ColumnDescriptor col, string texto) =>
        col.Item().PaddingTop(2).Text(t =>
        {
            t.Justify();
            foreach (var (segment, bold) in SplitKeywords(texto, MandatoKeywords))
            {
                var span = t.Span(segment);
                if (bold)
                    span.Bold();
            }
        });

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

    /// <summary>
    /// Identificación del MANDANTE bajo su firma (HU #11047). El organismo de tránsito necesita poder
    /// contactarlo y verificar su identidad sin abrir otro documento, así que el bloque lleva el orden y
    /// los campos que pidió el negocio:
    /// <code>
    ///   EMPRESA: BANCOLOMBIA S.A.S          (solo persona jurídica)
    ///   NIT: 890903938                      (solo persona jurídica)
    ///   NOMBRE: Juan Felipe Montoya
    ///   CÉDULA DE CIUDADANÍA: 1038409485
    ///   CELULAR: 3112789718
    ///   CORREO ELECTRÓNICO: correo@dominio
    /// </code>
    /// Antes imprimía NOMBRE/documento/EMPRESA/NIT —sin celular ni correo y en otro orden—, mientras el
    /// bloque de la solicitud virtual (<see cref="SolicitudVirtualPdfGenerator"/>) ya traía el contacto.
    /// En persona jurídica el nombre y el documento son los del REPRESENTANTE LEGAL, que es quien firma.
    /// </summary>
    internal static IEnumerable<string> MandanteIdentificacion(DocumentParte? parte, bool esJuridica)
    {
        if (esJuridica)
        {
            yield return $"EMPRESA: {Empresa(parte)}";
            yield return $"NIT: {Nit(parte)}";
            yield return $"NOMBRE: {RlNombre(parte)}";
            yield return $"{MapDoc(parte?.RepresentanteLegalTipoDoc).ToUpperInvariant()}: {RlDoc(parte)}";
        }
        else
        {
            yield return $"NOMBRE: {PnNombre(parte)}";
            yield return $"{MapDoc(parte?.DocumentType).ToUpperInvariant()}: {PnDoc(parte)}";
        }

        yield return $"CELULAR: {Val(parte?.Phone, "___")}";
        yield return $"CORREO ELECTRÓNICO: {Val(parte?.Email, "___")}";
    }

    /// <summary>
    /// Identificación del MANDATARIO bajo su firma. Es siempre una persona natural (el firmante del OT),
    /// así que no lleva empresa ni NIT. Se etiquetan los campos igual que en el bloque del mandante
    /// (HU #11047), porque ambos van uno al lado del otro en la misma fila del documento.
    /// <para>El contacto del mandatario NO se imprime: <c>MandatarioFirmante</c> no lo transporta, y el
    /// dato de contacto que el organismo necesita es el del mandante (quien otorga el poder).</para>
    /// </summary>
    internal static IEnumerable<string> MandatarioIdentificacion(
        MandatarioFirmante? mandatario, MandatoData? data = null)
    {
        // HU #11205 (AC2) — con empresa relacionada, quien firma a mano es la empresa: bajo la línea va
        // su razón social y su NIT. Poner ahí la cédula de la persona firmante haría que el documento
        // dijera que firmó alguien distinto de quien lo hace.
        if (data is not null && MandatarioEsEmpresa(data)
            && !string.IsNullOrWhiteSpace(data.InstitutionalMandataryName))
        {
            yield return $"RAZÓN SOCIAL: {data.InstitutionalMandataryName.Trim()}";
            yield return $"NIT: {Val(data.InstitutionalMandataryNit, "___")}";
            yield break;
        }

        var (nombre, documento) = MandatarioTexto(mandatario);
        yield return $"NOMBRE: {nombre}";
        yield return $"CÉDULA DE CIUDADANÍA: {documento}";
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
