using System.Runtime.CompilerServices;
using Flit.Infrastructure.Documents.Branding;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Tramites.ValueObjects;
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
    private static string SiglaIndemnidad(MandatoData data)
    {
        if (!data.PartesVisibles)
            return "___";
        if (!string.IsNullOrWhiteSpace(data.MandatarySigla))
            return data.MandatarySigla.Trim();
        return EsOficina(data, MandatoSystemOfficeTemplates.SabanetaOfficeCode) ? SetsaSigla : "___";
    }

    private static string IndemneUtOMandatario(string inst) =>
        inst == "___" ? "EL MANDATARIO" : $"la {inst}";

    private static DocumentParte? ParteEnCuerpo(MandatoData data)
    {
        var parte = data.Tramite.Mandante;
        if (data.PartesVisibles)
            return parte;
        if (parte is null)
            return null;
        return parte with
        {
            Nombre = null,
            Documento = null,
            Email = null,
            Phone = null,
            Address = null,
            RepresentanteLegalNombre = null,
            RepresentanteLegalTipoDoc = null,
            RepresentanteLegalDocumento = null,
        };
    }

    private static MandatarioFirmante? MandatarioEnCuerpo(MandatoData data) =>
        data.PartesVisibles ? data.Mandatario : null;

    private static bool EsOficina(MandatoData data, string officeCode) =>
        string.Equals(
            data.Tramite.Organismo.Codigo?.Trim(),
            officeCode,
            StringComparison.OrdinalIgnoreCase);

    private static string InstNombre(MandatoData data, string fallback, string officeCodeForFallback)
    {
        if (!data.PartesVisibles)
            return "___";
        if (!string.IsNullOrWhiteSpace(data.InstitutionalMandataryName))
            return data.InstitutionalMandataryName.Trim();
        return EsOficina(data, officeCodeForFallback) ? fallback : "___";
    }

    private static string InstNit(MandatoData data, string fallback, string officeCodeForFallback)
    {
        if (!data.PartesVisibles)
            return "___";
        if (!string.IsNullOrWhiteSpace(data.InstitutionalMandataryNit))
            return data.InstitutionalMandataryNit.Trim();
        return EsOficina(data, officeCodeForFallback) ? fallback : "___";
    }

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
        var parte = ParteEnCuerpo(data);
        var esJuridica = tramite.Mandante?.EsJuridica ?? false;
        var variante = MandatoTemplateResolver.Resolve(data.TemplateCode);

        // Objeto {{tramite}}: tres capas (docs/ot/mandato/REGLAS-OBJETO-TRES-CAPAS.md).
        var nombreTramite = ComponerObjeto(data);

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
        var parte = ParteEnCuerpo(data);
        var esJuridica = tramite.Mandante?.EsJuridica ?? false;
        // Fix negrita — el cuerpo del editor lo escribe el tenant en texto libre, pero los VALORES que
        // sustituimos ahí son siempre los mismos placeholders {{...}} conocidos (no texto libre): se
        // marcan en negrita en el momento de sustituirlos, igual que en la plantilla del sistema.
        var lineas = ApplyPlaceholdersSegmented(data.CustomTemplateBody!, data);

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
                    foreach (var linea in lineas)
                        RenderLineaEditor(col, linea);
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
        var parte = ParteEnCuerpo(data);
        var esJuridica = tramite.Mandante?.EsJuridica ?? false;

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

    internal static string ComponerObjeto(MandatoData data)
    {
        var tramite = data.Tramite;
        var code = string.IsNullOrWhiteSpace(tramite.ProcedureTypeCode)
            ? tramite.TipologiaCodigo
            : tramite.ProcedureTypeCode;
        return MandatoObjetoComposer.Componer(
            MandatoTramiteIdentity.NombreObjeto(
                tramite.ProcedureTypeName,
                tramite.ProcedureTypeCode,
                tramite.ProcedureFamily,
                tramite.TipologiaCodigo,
                tramite.Modalidad),
            data.Transformaciones,
            tramite.PrendaMarking,
            code);
    }

    /// <summary>
    /// Sustituye los placeholders <c>{{...}}</c> del cuerpo editado por el OT, línea por línea, marcando
    /// en negrita el VALOR sustituido (no el resto del texto libre del tenant).
    /// <para>Este cuerpo lo escribe el administrador del OT en texto libre, así que no se puede componer
    /// por segmentos "en el momento de interpolar" como las plantillas del sistema (no hay
    /// interpolación: es texto ya escrito). Lo que SÍ es seguro marcar por construcción es el TOKEN, no
    /// el contenido del valor — es sintaxis nuestra (<c>{{placa}}</c>, <c>{{mandante_nombre}}</c>, …), fija
    /// y conocida de antemano, y no cambia de un trámite a otro como sí cambian el NIT o la cédula (la
    /// causa original del defecto). Por eso NO es el mismo antipatrón que <c>MandatoKeywords</c>: aquí se
    /// busca la SINTAXIS del placeholder, no el VALOR que reemplazó a un placeholder en un trámite
    /// anterior.</para>
    /// </summary>
    private static List<IReadOnlyList<ParrafoSegmento>> ApplyPlaceholdersSegmented(string body, MandatoData data)
    {
        var tramite = data.Tramite;
        var parte = ParteEnCuerpo(data);
        var nombreTramite = ComponerObjeto(data);
        var (mandNombre, mandDoc) = MandatarioTexto(MandatarioEnCuerpo(data));

        var reemplazos = new (string Token, string Valor)[]
        {
            ("{{placa}}", Val(tramite.Placa, "___")),
            ("{{tramite}}", nombreTramite),
            ("{{organismo}}", Val(tramite.Organismo.Nombre, "___")),
            ("{{ciudad}}", tramite.Organismo.Ciudad?.Trim() ?? string.Empty),
            ("{{fecha}}", FormatFechaEs(tramite.FechaTramite ?? DateTime.UtcNow.AddHours(-5))),
            ("{{mandante_nombre}}", Val(parte?.Nombre, "___")),
            ("{{mandante_documento}}", Val(parte?.Documento, "___")),
            ("{{mandatario_nombre}}", mandNombre),
            ("{{mandatario_documento}}", mandDoc),
            ("{{mandatario_institucional}}", Val(
                data.PartesVisibles ? data.InstitutionalMandataryName : null, "___")),
            ("{{mandatario_nit}}", Val(
                data.PartesVisibles ? data.InstitutionalMandataryNit : null, "___")),
        };

        return body.Split('\n')
            .Select(linea => (IReadOnlyList<ParrafoSegmento>)SplitPlaceholders(linea.TrimEnd(), reemplazos))
            .ToList();
    }

    /// <summary>
    /// Divide una línea en segmentos normales y en negrita reemplazando los placeholders CONOCIDOS
    /// (<c>{{token}}</c>) por su valor. Toma siempre el token más largo que calce en cada posición.
    /// </summary>
    internal static List<ParrafoSegmento> SplitPlaceholders(
        string texto, (string Token, string Valor)[] reemplazos)
    {
        var segmentos = new List<ParrafoSegmento>();
        var buffer = new System.Text.StringBuilder();
        var i = 0;
        while (i < texto.Length)
        {
            var match = reemplazos.FirstOrDefault(r =>
                i + r.Token.Length <= texto.Length
                && string.Compare(texto, i, r.Token, 0, r.Token.Length, StringComparison.OrdinalIgnoreCase) == 0);
            if (match.Token is not null)
            {
                if (buffer.Length > 0)
                {
                    segmentos.Add(new ParrafoSegmento(buffer.ToString(), false));
                    buffer.Clear();
                }

                // Mismo criterio que MandatoParrafoHandler: "___" (sin dato todavía) no se resalta.
                segmentos.Add(new ParrafoSegmento(match.Valor, match.Valor != "___"));
                i += match.Token.Length;
            }
            else
            {
                buffer.Append(texto[i]);
                i++;
            }
        }

        if (buffer.Length > 0)
            segmentos.Add(new ParrafoSegmento(buffer.ToString(), false));

        return segmentos;
    }

    /// <summary>Una línea del cuerpo del editor: sin justificar (texto libre del tenant), a 9pt.</summary>
    private static void RenderLineaEditor(ColumnDescriptor col, IReadOnlyList<ParrafoSegmento> linea) =>
        col.Item().Text(t =>
        {
            foreach (var seg in linea)
            {
                var span = t.Span(seg.Texto).FontSize(9);
                if (seg.Negrita)
                    span.Bold();
            }
        });

    private static List<IReadOnlyList<ParrafoSegmento>> BuildParrafos(
        MandatoData data, DocumentParte? parte, bool esJuridica, MandatoVariante variante,
        string nombreTramite, string placa, string ot, string ciudad, string fecha) => variante switch
        {
            MandatoVariante.Sabaneta => Sabaneta(data, parte, esJuridica, nombreTramite, placa, ciudad, fecha),
            MandatoVariante.Bello => Bello(data, parte, esJuridica, nombreTramite, placa, ot, ciudad, fecha),
            MandatoVariante.Municipio => Municipio(data, parte, esJuridica, nombreTramite, placa, ciudad, fecha),
            _ => Generico(data, parte, esJuridica, nombreTramite, placa, ot, ciudad, fecha),
        };

    // ---- Genérica: mandatario persona (firmante del OT). Ambos firman. ----
    private static List<IReadOnlyList<ParrafoSegmento>> Generico(
        MandatoData data, DocumentParte? parte, bool esJuridica,
        string nombreTramite, string placa, string ot, string ciudad, string fecha)
    {
        var mandatario = MandatarioTexto(MandatarioEnCuerpo(data));
        var intro = esJuridica
            ? Parrafo(
                Frag($"Yo, {RlNombre(parte)}, mayor de edad, identificado con {RlTipo(parte)} No. {RlDoc(parte)}, "),
                Frag($"en representación legal de {Empresa(parte)}, con NIT No. {Nit(parte)}, según lo acredita la "),
                Frag($"escritura pública y/o el certificado de existencia y representación expedido por la Cámara de "),
                Frag($"Comercio de {Camara(data)} y quien para los efectos del presente contrato se denominará {"EL MANDANTE"}. "),
                Frag($"Y de {mandatario.Nombre} identificado con la cédula de ciudadanía No {mandatario.Documento}, "),
                Frag($"quien para los efectos del presente contrato se denominará {"EL MANDATARIO"}, hemos acordado suscribir "),
                Frag($"{Plano(ResolucionesCc())}"))
            : Parrafo(
                Frag($"Yo, {PnNombre(parte)}, mayor de edad, identificado con {PnTipo(parte)} número No {PnDoc(parte)}, "),
                Frag($"quien para los efectos del presente contrato se denominará {"EL MANDANTE"}. "),
                Frag($"Y de {mandatario.Nombre} identificado con la cédula de ciudadanía No {mandatario.Documento}, "),
                Frag($"quien para los efectos del presente contrato se denominará {"EL MANDATARIO"}, hemos acordado suscribir "),
                Frag($"{Plano(ResolucionesCc())}"));

        return
        [
            intro,
            PrimeraObjeto(nombreTramite),
            Parrafo($"Identificado con placas {placa}. Ante el organismo de tránsito de {ot}."),
            Parrafo(
                Frag($"Como consecuencia, {"EL MANDATARIO"} queda facultado para realizar todas las gestiones propias de este "),
                Frag($"mandato y en especial para representar, notificarse, recibir, impugnar, desistir, sustituir, reasumir, "),
                Frag($"pedir, conciliar o asumir obligaciones en nombre del {"MANDANTE"}.")),
            SegundaObligaciones(),
            CierreFirma(fecha, ciudad),
        ];
    }

    // ---- Sabaneta: mandatario institucional UT-SETSA. Solo firma el MANDANTE. ----
    // Tipo Abierto (Manual + sin firmante): el cuerpo usa la redacción genérica con placeholders,
    // para no rellenar la UT cuando el negocio pide líneas abiertas.
    private static List<IReadOnlyList<ParrafoSegmento>> Sabaneta(
        MandatoData data, DocumentParte? parte, bool esJuridica,
        string nombreTramite, string placa, string ciudad, string fecha)
    {
        if (data.ModoFirmaMandatario == MandatarioFirmaModo.Manual && data.Mandatario is null)
        {
            var ot = Val(data.Tramite.Organismo.Nombre, "___");
            return Generico(data, parte, esJuridica, nombreTramite, placa, ot, ciudad, fecha);
        }

        var inst = InstNombre(data, SetsaNombre, MandatoSystemOfficeTemplates.SabanetaOfficeCode);
        var nit = InstNit(data, SetsaNit, MandatoSystemOfficeTemplates.SabanetaOfficeCode);
        var intro = esJuridica
            ? Parrafo(
                Frag($"Yo, {RlNombre(parte)}, mayor de edad, identificado con {RlTipo(parte)} número No {RlDoc(parte)}, "),
                Frag($"en representación legal de {Empresa(parte)}, con NIT No. {Nit(parte)}, según lo acredita la "),
                Frag($"escritura pública y/o el certificado de existencia y representación expedido por la Cámara de "),
                Frag($"Comercio de {Camara(data)} y quien para los efectos del presente contrato se denominará {"EL MANDANTE"}. "),
                Frag($"Y de {inst}, con NIT N° {nit}, quien para efectos del presente contrato se denominará {"EL MANDATARIO"}, "),
                Frag($"hemos acordado suscribir el siguiente contrato de mandato mediante el cual el mandatario se hace "),
                Frag($"cargo de la gestión de realizar el trámite de {nombreTramite} del vehículo de placas: {placa}, "),
                Frag($"por cuenta y riesgo del mandante."))
            : Parrafo(
                Frag($"Yo, {PnNombre(parte)}, mayor de edad, identificado con {PnTipo(parte)} número No {PnDoc(parte)}, "),
                Frag($"en representación propia y quien para los efectos del presente contrato se denominará {"EL MANDANTE"}. "),
                Frag($"Y de {inst}, con NIT N° {nit}, quien para efectos del presente contrato se denominará {"EL MANDATARIO"}, "),
                Frag($"hemos acordado suscribir el siguiente contrato de mandato mediante el cual el mandatario se hace "),
                Frag($"cargo de la gestión de realizar el trámite de {nombreTramite} del vehículo de placas: {placa}, "),
                Frag($"por cuenta y riesgo del mandante."));

        return
        [
            intro,
            Parrafo(
                Frag($"{"OBLIGACIONES DEL MANDANTE"}: {"EL MANDANTE"} declara que la información contenida en los documentos que se "),
                Frag($"anexan a la solicitud del trámite es veraz y auténtica, razón por la que se hace responsable ante las "),
                Frag($"autoridades competentes de cualquier irregularidad que los mismos puedan contener; al igual dejando "),
                Frag($"indemne a la {Plano(SiglaIndemnidad(data))} de cualquier responsabilidad en los que se ve comprometido la confidencialidad y "),
                Frag($"divulgación de la información legalmente protegida mediante los parámetros y disposiciones de la ley "),
                Frag($"1581 del 2012 y demás normas que se dicten en la materia.")),
            CierreFirma(fecha, ciudad),
        ];
    }

    // ---- Bello: mandatario persona, representante legal de la UT-MAB. Ambos firman. ----
    private static List<IReadOnlyList<ParrafoSegmento>> Bello(
        MandatoData data, DocumentParte? parte, bool esJuridica,
        string nombreTramite, string placa, string ot, string ciudad, string fecha)
    {
        var inst = InstNombre(data, MabNombre, MandatoSystemOfficeTemplates.BelloOfficeCode);
        var nit = InstNit(data, MabNit, MandatoSystemOfficeTemplates.BelloOfficeCode);
        var mandatario = MandatarioTexto(MandatarioEnCuerpo(data));
        var nombraUt = inst != "___";
        var introMandatario = nombraUt
            ? Frag($"Y de la otra parte, {mandatario.Nombre} identificado con la cédula de ciudadanía No {mandatario.Documento}, Representante Legal de {inst}, con NIT No. {nit}, quien para efectos del presente contrato se denominará {"EL MANDATARIO"}, hemos acordado suscribir el siguiente contrato de mandato mediante el cual el mandatario se hace cargo de la gestión de realizar el trámite según las siguientes cláusulas.")
            : Frag($"Y de {mandatario.Nombre} identificado con la cédula de ciudadanía No {mandatario.Documento}, quien para los efectos del presente contrato se denominará {"EL MANDATARIO"}, hemos acordado suscribir el siguiente contrato de mandato mediante el cual el mandatario se hace cargo de la gestión de realizar el trámite según las siguientes cláusulas.");
        var intro = esJuridica
            ? Parrafo(
                Frag($"Yo, {RlNombre(parte)}, mayor de edad, identificado con {RlTipo(parte)} número No {RlDoc(parte)}, "),
                Frag($"en representación legal de {Empresa(parte)}, con NIT No. {Nit(parte)}, según lo acredita la "),
                Frag($"escritura pública y/o el Certificado de Existencia y Representación expedido por la Cámara de "),
                Frag($"Comercio de {Camara(data)} y quien para los efectos del presente contrato se denominará {"EL MANDANTE"}. "),
                introMandatario)
            : Parrafo(
                Frag($"Yo, {PnNombre(parte)}, mayor de edad, identificado con {PnTipo(parte)} número No {PnDoc(parte)}, "),
                Frag($"quien para los efectos del presente contrato se denominará {"EL MANDANTE"}. "),
                introMandatario);

        return
        [
            intro,
            PrimeraObjeto(nombreTramite),
            Parrafo($"Identificado con placas {placa}. Ante el organismo de tránsito de {ot}."),
            Parrafo(
                Frag($"{"OBLIGACIONES DEL MANDANTE"}: {"EL MANDANTE"} declara que la información contenida en los documentos que se "),
                Frag($"anexan a la solicitud del trámite es veraz y auténtica, razón por la que se hace responsable ante las "),
                Frag($"autoridades competentes de cualquier irregularidad que los mismos puedan contener; de igual modo, "),
                Frag($"declara que deja indemne a {IndemneUtOMandatario(inst)} de cualquier responsabilidad civil o penal, asimismo, {"EL MANDANTE"} "),
                Frag($"de forma expresa asevera que deja impoluto a {"EL MANDATARIO"} en todos los casos que se vea comprometido "),
                Frag($"la confidencialidad y divulgación de la información legalmente protegida mediante los parámetros y "),
                Frag($"disposiciones de la ley 1581 del 2012 y demás normas que se dicten en la materia.")),
            CierreFirma(fecha, ciudad),
        ];
    }

    // ---- Municipio (Envigado / Funza / Medellín): redacción corta PN; PJ con RL. Ambos firman. ----
    private static List<IReadOnlyList<ParrafoSegmento>> Municipio(
        MandatoData data, DocumentParte? parte, bool esJuridica,
        string nombreTramite, string placa, string ciudad, string fecha)
    {
        var mandatario = MandatarioTexto(MandatarioEnCuerpo(data));
        var intro = esJuridica
            ? Parrafo(
                Frag($"Yo, {RlNombre(parte)}, mayor de edad, identificado con {RlTipo(parte)} número No {RlDoc(parte)}, "),
                Frag($"en representación legal de {Empresa(parte)}, con NIT No. {Nit(parte)}, quien para los efectos "),
                Frag($"del presente contrato se denominará {"EL MANDANTE"}. "),
                Frag($"Y de {mandatario.Nombre} identificado con la cédula de ciudadanía No {mandatario.Documento}, "),
                Frag($"quien para los efectos del presente contrato se denominará {"EL MANDATARIO"}, hemos acordado "),
                Frag($"suscribir el siguiente contrato de mandato mediante el cual el mandatario se hace cargo de la "),
                Frag($"radicación y reclamación del trámite de {nombreTramite} vehículo de placa: {placa}, por "),
                Frag($"cuenta y riesgo del mandante."))
            : Parrafo(
                Frag($"Yo, {PnNombre(parte)}, mayor de edad, identificado con {PnTipo(parte)} número No {PnDoc(parte)}, "),
                Frag($"quien para los efectos del presente contrato se denominará {"EL MANDANTE"}. "),
                Frag($"Y de {mandatario.Nombre} identificado con la cédula de ciudadanía No {mandatario.Documento}, "),
                Frag($"quien para los efectos del presente contrato se denominará {"EL MANDATARIO"}, hemos acordado "),
                Frag($"suscribir el siguiente contrato de mandato mediante el cual el mandatario se hace cargo de la "),
                Frag($"radicación y reclamación del trámite de {nombreTramite} vehículo de placa: {placa}, por "),
                Frag($"cuenta y riesgo del mandante."));

        return
        [
            intro,
            Parrafo(
                Frag($"{"OBLIGACIONES DEL MANDANTE"}: {"EL MANDANTE"} declara que la información contenida en los documentos que se "),
                Frag($"anexan a la solicitud del trámite es veraz y auténtica, razón por la que se hace responsable ante la "),
                Frag($"autoridad competente de cualquier irregularidad que los mismos puedan contener.")),
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

    private static List<ParrafoSegmento> PrimeraObjeto(string nombreTramite) =>
        Parrafo(
            Frag($"{"PRIMERA: OBJETO DEL MANDATO"}. {"EL MANDANTE"} confiere a {"EL MANDATARIO"} poder amplio y suficiente para que en su "),
            Frag($"nombre y representación adelante, radique y reclame ante el organismo de tránsito el trámite de {nombreTramite} "),
            Frag($"respecto del automotor."));

    private static List<ParrafoSegmento> SegundaObligaciones() =>
        Parrafo(
            Frag($"{"SEGUNDA: OBLIGACIONES DEL MANDANTE"}. {"EL MANDANTE"} declara que la información contenida en los documentos que "),
            Frag($"se anexan a la solicitud del trámite es veraz y auténtica, razón por la que se hace responsable ante la "),
            Frag($"autoridad competente de cualquier irregularidad que los mismos puedan contener."));

    /// <summary>Cláusula de cierre: menciona la ciudad solo si se conoce (HU #11016).</summary>
    private static List<ParrafoSegmento> CierreFirma(string fecha, string ciudad) =>
        string.IsNullOrEmpty(ciudad)
            ? Parrafo($"Dicho contrato se firmó entre las partes el {fecha}.")
            : Parrafo($"Dicho contrato se firmó entre las partes el {fecha} en la ciudad de {ciudad}.");

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
                    manual ? null : MandatarioEnCuerpo(data)?.FirmaImagen,
                    manual ? null : MandatarioEnCuerpo(data)?.SelloIdentidad,
                    MandatarioIdentificacion(MandatarioEnCuerpo(data), data),
                    FlitFirmaLinea.Underscores,
                    selloBaul: manual ? null : SelloBaulDe(MandatarioEnCuerpo(data)),
                    etiquetaSinEstampa: "Sin firmar");
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
            selloBaul: FlitFirmaBaulSello.Resolve(tramite.FirmaBaulMetadatos, parte?.Rol, incluirIdentificacion: false),
            etiquetaSinEstampa: "Sin firmar");
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

    // ---- Composición de párrafos por SEGMENTOS (fix de la negrita inconsistente) -------------------
    //
    // Antes, cada párrafo se armaba como una única cadena con los valores YA interpolados dentro
    // (p. ej. $"...identificado con {PnTipo(parte)} número No {PnDoc(parte)}..."), y la negrita se
    // reconstruía DESPUÉS buscando coincidencias EXACTAS contra una lista fija de palabras clave
    // (MandatoKeywords: los encabezados de cláusula, "MANDANTE"/"MANDATARIO" y los marcadores del
    // preview). Esa lista nunca contenía los valores reales de cada trámite (el NIT, la cédula, el
    // nombre…), que cambian en cada mandato — por eso el NIT de una empresa podía salir en negrita
    // (si por azar coincidía con algo de la lista) mientras la cédula de al lado no.
    //
    // Ahora la negrita es una propiedad de la COMPOSICIÓN, no una búsqueda a posteriori: cada párrafo
    // es una lista de fragmentos (Frag(...)) y cada fragmento es una interpolación normal donde TODO lo
    // que va entre `{ }` sale en negrita por defecto — sea un valor del trámite (RlNombre(parte),
    // Nit(parte), placa…) o una palabra estructural escrita a propósito entre llaves (p. ej.
    // {"EL MANDANTE"}). El texto que queda FUERA de las llaves nunca se resalta. Quien agregue un
    // párrafo nuevo tiene que hacer un esfuerzo activo para que un valor NO salga en negrita
    // (envolverlo con Plano(...)) — el olvido ya no reproduce el defecto original, que era al revés.
    // internal (no private): Flit.Infrastructure.Tests (InternalsVisibleTo) prueba la composición por
    // segmentos directamente, igual que ya hace con MandanteIdentificacion/MandatarioIdentificacion —
    // este generador no expone su texto a un lector de PDF, así que el contrato de "qué queda en
    // negrita" solo se puede verificar aquí, no inspeccionando bytes.
    internal readonly record struct ParrafoSegmento(string Texto, bool Negrita);

    /// <summary>
    /// Envuelve un texto compuesto que NO es un valor del trámite y por eso NO debe resaltarse al
    /// interpolarlo (p. ej. la cita larga de resoluciones de <see cref="ResolucionesCc"/>, o la sigla de
    /// la unión temporal en la cláusula de obligaciones de Sabaneta).
    /// </summary>
    internal static ParrafoLiteral Plano(string texto) => new(texto);

    internal readonly record struct ParrafoLiteral(string Texto);

    /// <summary>
    /// Handler de interpolación de un fragmento de párrafo: el texto literal de la plantilla queda
    /// normal y cada interpolación <c>{ ... }</c> sale en negrita por defecto, salvo que se envuelva con
    /// <see cref="Plano"/>. Al ser el mecanismo por el que CUALQUIER valor entra al párrafo, un valor
    /// nuevo no puede "olvidarse" de marcar: si se interpola, ya sale en negrita.
    /// </summary>
    [InterpolatedStringHandler]
    internal struct MandatoParrafoHandler
    {
        private readonly List<ParrafoSegmento> _segmentos;

        public MandatoParrafoHandler(int literalLength, int formattedCount)
        {
            _segmentos = new List<ParrafoSegmento>((formattedCount * 2) + 1);
        }

        public void AppendLiteral(string s)
        {
            if (s.Length > 0)
                _segmentos.Add(new ParrafoSegmento(s, false));
        }

        // Por defecto, NEGRITA: cualquier valor interpolado sale resaltado, sin que el generador tenga
        // que reconocerlo después por su contenido. Única excepción: "___" es el marcador de "todavía
        // sin dato" que usa Val(...) en TODO este generador (mandato tipo Abierto sin firmante, placa o
        // ciudad aún no resueltas…) — no es un valor del trámite, así que se deja igual que el resto del
        // texto: negritarlo no aporta nada y, en la línea del mandatario, se ve como un guion "raro" más
        // grueso que el resto.
        public void AppendFormatted(string? valor)
        {
            if (string.IsNullOrEmpty(valor))
                return;
            _segmentos.Add(new ParrafoSegmento(valor, valor != "___"));
        }

        // Escape explícito para texto compuesto que NO es un valor del trámite (Plano(...)).
        public void AppendFormatted(ParrafoLiteral literal)
        {
            if (!string.IsNullOrEmpty(literal.Texto))
                _segmentos.Add(new ParrafoSegmento(literal.Texto, false));
        }

        public List<ParrafoSegmento> GetSegments() => _segmentos;
    }

    /// <summary>Un párrafo compuesto de un único fragmento interpolado.</summary>
    internal static List<ParrafoSegmento> Parrafo(MandatoParrafoHandler texto) => texto.GetSegments();

    /// <summary>
    /// Un párrafo compuesto de varios fragmentos — equivalente a unirlos con "+" como antes, pero sin
    /// perder la negrita de cada uno: cada <see cref="Frag"/> es su propia interpolación, resuelta por
    /// <see cref="MandatoParrafoHandler"/>.
    /// </summary>
    internal static List<ParrafoSegmento> Parrafo(params IReadOnlyList<ParrafoSegmento>[] fragmentos) =>
        fragmentos.SelectMany(f => f).ToList();

    /// <summary>Un fragmento de párrafo — ver <see cref="Parrafo(IReadOnlyList{ParrafoSegmento}[])"/>.</summary>
    internal static List<ParrafoSegmento> Frag(MandatoParrafoHandler texto) => texto.GetSegments();

    // HU #11034 — párrafos JUSTIFICADOS y compactos: el contrato debe caber en una sola hoja, firmas
    // incluidas, y el texto justificado es lo que espera un documento legal.
    private static void RenderParrafo(ColumnDescriptor col, IReadOnlyList<ParrafoSegmento> parrafo) =>
        col.Item().PaddingTop(2).Text(t =>
        {
            t.Justify();
            foreach (var seg in parrafo)
            {
                var span = t.Span(seg.Texto);
                if (seg.Negrita)
                    span.Bold();
            }
        });

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
