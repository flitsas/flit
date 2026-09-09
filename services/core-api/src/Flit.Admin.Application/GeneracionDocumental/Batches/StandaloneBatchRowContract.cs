using Flit.Admin.Domain.GeneracionDocumental;

namespace Flit.Admin.Application.GeneracionDocumental.Batches;

/// <summary>A qué filas le sirve una columna. Es lo que la hoja «Instrucciones» pone en claro.</summary>
public enum StandaloneBatchColumnScope
{
    /// <summary>La leen las dos clases de fila.</summary>
    Ambas,

    /// <summary>Solo <c>certificado_rues</c>.</summary>
    Rues,

    /// <summary>Solo <c>transferencia_dominio_generada</c>, en cualquier escenario.</summary>
    Transferencia,

    /// <summary>Solo el escenario B —y, en dos casos, el C— del documento de transferencia.</summary>
    Leasing,
}

/// <summary>
/// Una columna de la plantilla XLSX v1 (CF-11).
/// </summary>
/// <param name="Header">
/// Encabezado literal de la fila 1. Es el contrato: un encabezado distinto —cambiado de orden,
/// renombrado o con columnas de más— hace fallar la carga con <c>template_invalid</c>.
/// </param>
public sealed record StandaloneBatchColumn(string Header)
{
    /// <summary>
    /// Columna de fecha. <b>Se sigue declarando como TEXTO en el XLSX</b> (formato ISO
    /// <c>AAAA-MM-DD</c>): la marca solo le dice al parser que una celda NUMÉRICA aquí es un serial
    /// de fecha de Excel y debe rechazarse como error de fila, en vez de intentar convertirla.
    /// Convertir seriales es la vía rápida a un documento con la fecha equivocada.
    /// </summary>
    public bool IsDate { get; init; }

    /// <summary>
    /// Qué escribir en esta columna, en la lengua del usuario. Es <c>required</c> a propósito: sin
    /// esto no compila, así que <b>ninguna columna puede nacer sin explicación</b>. Se emite dos
    /// veces en el XLSX —como mensaje emergente de la celda y como fila del diccionario de la hoja
    /// «Instrucciones»— y por eso no debe pasar de
    /// <see cref="StandaloneBatchTemplate.MaxGuiaLength"/> caracteres: Excel trunca el emergente.
    /// </summary>
    public required string Guia { get; init; }

    /// <summary>A qué clase de fila le sirve la columna.</summary>
    public required StandaloneBatchColumnScope Aplica { get; init; }

    /// <summary>
    /// Valores admitidos, cuando forman catálogo cerrado. Se convierten en el desplegable de la
    /// columna y salen de las constantes del dominio, nunca escritos a mano aquí: un desplegable
    /// que ofrece un valor que el backend descarta produce documentos incompletos sin un solo error
    /// visible. <c>null</c> = texto libre.
    /// </summary>
    public IReadOnlyList<string>? Opciones { get; init; }
}

/// <summary>
/// Plantilla de carga masiva <b>v1</b> (CF-11/CF-12, Feature #12201 I3). Es la fuente de verdad
/// tanto del XLSX que se descarga por <c>GET /lotes/plantilla</c> como de la validación del
/// encabezado en <c>POST /lotes</c>: el generador y el validador leen la MISMA lista, así que no
/// pueden divergir.
///
/// <para><b>Todas las columnas se emiten como texto</b> (§8.4 del diseño). Es la mitigación de
/// contrato que hace tratable el parser SAX sin ClosedXML ni EPPlus: sin ella habría que interpretar
/// tipos numéricos, formatos y seriales de fecha celda por celda.</para>
///
/// <para>Una sola hoja cubre los dos tipos de documento (CF-12): las columnas que no aplican al tipo
/// de la fila se dejan vacías. Un XLSX por tipo obligaría a cargar dos archivos para un mismo lote.</para>
///
/// <para><b>Los catálogos NO se escriben aquí.</b> Cada lista de valores apunta a la constante del
/// dominio que ya usa el generador —<see cref="TransferScenario"/>,
/// <see cref="TransferJuridicalTitle"/>, <see cref="TransferPurchaseOption"/>,
/// <see cref="TransferFiscalAssumption"/>—, de modo que el desplegable de la plantilla y lo que el
/// servidor acepta son literalmente el mismo dato.</para>
/// </summary>
public static class StandaloneBatchTemplate
{
    /// <summary>Versión declarada del contrato. Hoy solo existe una.</summary>
    public const string Version = "v1";

    public const string SheetName = "Lote v1";

    /// <summary>Hoja de ayuda del libro. Va DESPUÉS de la de datos; ver la nota del generador.</summary>
    public const string GuideSheetName = "Instrucciones";

    /// <summary>Hoja oculta que alimenta los desplegables.</summary>
    public const string ListsSheetName = "Listas";

    /// <summary>Tope duro de filas de datos (CF-11). 101 responde <c>too_many_rows</c>.</summary>
    public const int MaxRows = 100;

    /// <summary>
    /// Tope del mensaje emergente de una celda en Excel. Pasarse no da error: Excel <b>trunca</b> el
    /// texto, que es peor —la guía quedaría cortada a media frase—. Un test lo vigila.
    /// </summary>
    public const int MaxGuiaLength = 250;

    /// <summary>Casilla de declaración. El parser lee SI, SÍ, S, X, TRUE y 1 como afirmativo.</summary>
    public static IReadOnlyList<string> SiNo { get; } = ["SI", "NO"];

    /// <summary>
    /// Tipos de documento de una parte. El backend NO los restringe —los transcribe—, así que este
    /// catálogo existe para que el lote ofrezca exactamente los mismos que el formulario individual
    /// (<c>ParteFields.tsx</c>) y no aparezcan documentos con abreviaturas inventadas.
    /// </summary>
    public static IReadOnlyList<string> TiposDocumento { get; } = ["CC", "CE", "PAS", "NIT"];

    /// <summary>Naturaleza de la parte: persona natural o jurídica.</summary>
    public static IReadOnlyList<string> TiposPersona { get; } = ["PN", "PJ"];

    /// <summary>Los dos documentos que emite el módulo.</summary>
    public static IReadOnlyList<string> TiposDocumentoEmitido { get; } =
    [
        StandaloneDocumentType.CertificadoRues,
        StandaloneDocumentType.TransferenciaDominioGenerada,
    ];

    /// <summary>Los tres escenarios del anexo §3.</summary>
    public static IReadOnlyList<string> Escenarios { get; } =
    [
        TransferScenario.TraspasoOrdinario,
        TransferScenario.UnilateralLeasing,
        TransferScenario.FinancieraATercero,
    ];

    /// <summary>Catálogo de <c>{{titulo_juridico}}</c> (anexo §5.4).</summary>
    public static IReadOnlyList<string> TitulosJuridicos { get; } =
    [
        TransferJuridicalTitle.Compraventa,
        TransferJuridicalTitle.DacionEnPago,
        TransferJuridicalTitle.Permuta,
        TransferJuridicalTitle.Donacion,
        TransferJuridicalTitle.Otro,
    ];

    /// <summary>Catálogo de <c>{{tipo_opcion_compra}}</c> del escenario B (anexo §5.5).</summary>
    public static IReadOnlyList<string> OpcionesDeCompra { get; } =
    [
        TransferPurchaseOption.Ejercida,
        TransferPurchaseOption.Automatica,
        TransferPurchaseOption.TerminacionContrato,
    ];

    // ── Columnas comunes ────────────────────────────────────────────────────────────────────────
    public const string DocumentType = "document_type";
    public const string Escenario = "escenario";

    // ── Certificado RUES ────────────────────────────────────────────────────────────────────────
    public const string Nit = "nit";

    // ── Vehículo (anexo §5.1) ───────────────────────────────────────────────────────────────────
    public const string Placa = "placa";

    // ── Negocio (anexo §5.4) ────────────────────────────────────────────────────────────────────
    public const string FechaFirma = "fecha_firma";

    // ── Leasing (anexo §5.5) ────────────────────────────────────────────────────────────────────
    public const string FechaTerminacion = "fecha_terminacion";

    // ── Régimen aplicable (CF-24, VB-07) ────────────────────────────────────────────────────────
    public const string RegimenNingunaAplica = "regimen_ninguna_aplica";
    public const string RegimenCondiciones = "regimen_condiciones";

    /// <summary>
    /// Las columnas de la v1, EN ORDEN. El orden es parte del contrato: el validador compara la
    /// secuencia completa, no un conjunto.
    /// </summary>
    public static IReadOnlyList<StandaloneBatchColumn> Columns { get; } =
    [
        new(DocumentType)
        {
            Aplica = StandaloneBatchColumnScope.Ambas,
            Opciones = TiposDocumentoEmitido,
            Guia = "Obligatoria. Qué documento emite esta fila. certificado_rues solo necesita el NIT; "
                + "transferencia_dominio_generada necesita además escenario, vehículo, partes y negocio. "
                + "Una fila con otro valor queda en error sin cancelar el lote.",
        },
        new(Escenario)
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Opciones = Escenarios,
            Guia = "Obligatoria en transferencia y vacía en RUES. A = traspaso ordinario (art. 5.3.2.1). "
                + "B = transferencia unilateral de leasing al locatario (art. 5.3.2.2). C = entidad "
                + "financiera a un tercero. Exactamente uno (VB-05).",
        },
        new(Nit)
        {
            Aplica = StandaloneBatchColumnScope.Rues,
            Guia = "Obligatoria en RUES. NIT SIN dígito de verificación y sin puntos ni guiones "
                + "(900123456). El dígito lo calcula FLIT, para que no puedan discrepar dentro del "
                + "mismo certificado.",
        },

        new(Placa)
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Obligatoria en transferencia. Seis caracteres sin guion ni espacios (ABC123). Un "
                + "formato distinto bloquea la fila con VB-02 (art. 5.1.8).",
        },
        new("marca")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Marca del vehículo tal como figura en la licencia de tránsito. Se transcribe "
                + "literalmente al documento.",
        },
        new("linea")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Línea del vehículo según la licencia de tránsito. Se transcribe literalmente.",
        },
        new("modelo_anio")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Año del modelo, cuatro dígitos (2019). Es texto, no un número: no le cambies el "
                + "formato a la celda.",
        },
        new("clase_vehiculo")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Clase según la licencia de tránsito: AUTOMÓVIL, CAMIONETA, MOTOCICLETA, REMOLQUE… "
                + "Se transcribe literalmente.",
        },
        new("tipo_carroceria")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Tipo de carrocería según la licencia de tránsito. Se transcribe literalmente.",
        },
        new("color")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Color del vehículo según la licencia de tránsito.",
        },
        new("no_motor")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Número de motor, sin espacios. Si el vehículo no lo tiene, deja la celda VACÍA: no "
                + "escribas N/A ni guiones, porque se transcribirían al documento.",
        },
        new("no_chasis")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Número de chasis, sin espacios. Si el vehículo no lo tiene, deja la celda vacía.",
        },
        new("no_serie")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Número de serie, sin espacios. Si el vehículo no lo tiene, deja la celda vacía.",
        },
        new("servicio")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Servicio según la licencia de tránsito: PARTICULAR, PÚBLICO u OFICIAL. Ojo: el "
                + "servicio público de pasajeros o mixto es condición especial y bloquea la fila (VB-07).",
        },
        new("no_licencia_transito")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Número de la licencia de tránsito del vehículo.",
        },
        new("organismo_transito")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Organismo de tránsito donde está matriculado el vehículo, con su nombre completo.",
        },

        new("transferente_tipo_persona")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Opciones = TiposPersona,
            Guia = "PN = persona natural, PJ = persona jurídica. El escenario clasifica la operación, NO "
                + "a las personas: en A, B y C puede haber de las dos.",
        },
        new("transferente_nombre")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Nombre completo o razón social de quien transfiere, tal como debe aparecer en el "
                + "documento.",
        },
        new("transferente_tipo_doc")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Opciones = TiposDocumento,
            Guia = "Tipo de documento de quien transfiere. Una persona jurídica usa NIT.",
        },
        new("transferente_no_doc")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Número sin puntos, guiones ni dígito de verificación. Transferente y adquirente no "
                + "pueden tener el mismo número: nadie se transfiere a sí mismo (VB-06).",
        },
        new("transferente_domicilio")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Ciudad de domicilio de quien transfiere.",
        },
        new("transferente_representante_legal")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Solo si el transferente es PJ: nombre del representante legal que firma. Déjala "
                + "vacía para una persona natural.",
        },
        new("transferente_cc_representante_legal")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Solo si el transferente es PJ: cédula del representante legal. Déjala vacía para una "
                + "persona natural.",
        },

        new("adquirente_tipo_persona")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Opciones = TiposPersona,
            Guia = "PN o PJ. En el ESCENARIO B no hay adquirente —el acto es unilateral— y todas las "
                + "columnas adquirente_ se dejan vacías: el destinatario va en las columnas locatario_.",
        },
        new("adquirente_nombre")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Nombre completo o razón social de quien adquiere. Vacía en el escenario B.",
        },
        new("adquirente_tipo_doc")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Opciones = TiposDocumento,
            Guia = "Tipo de documento de quien adquiere. Vacía en el escenario B.",
        },
        new("adquirente_no_doc")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Número sin puntos, guiones ni dígito de verificación. Vacía en el escenario B. No "
                + "puede coincidir con el del transferente (VB-06).",
        },
        new("adquirente_domicilio")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Ciudad de domicilio de quien adquiere. Vacía en el escenario B.",
        },
        new("adquirente_representante_legal")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Solo si el adquirente es PJ: nombre del representante legal. Vacía en el escenario B.",
        },
        new("adquirente_cc_representante_legal")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Solo si el adquirente es PJ: cédula del representante legal. Vacía en el escenario B.",
        },

        new("titulo_juridico")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Opciones = TitulosJuridicos,
            Guia = "Obligatoria en los escenarios A y C: qué negocio produce la transferencia. Solo "
                + "COMPRAVENTA exige precio en letras Y en números (VB-A-06); los demás describen la "
                + "contraprestación, y la donación no tiene.",
        },
        new("descripcion_titulo")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Detalle del título cuando eliges OTRO, y aclaración opcional en los demás. Se "
                + "transcribe al documento.",
        },
        new("precio_letras")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Obligatoria si titulo_juridico es COMPRAVENTA: el precio escrito en letras (VEINTE "
                + "MILLONES DE PESOS). DEBE ir vacía en el escenario B (VB-B-05).",
        },
        new("precio_numeros")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Obligatoria si titulo_juridico es COMPRAVENTA: el mismo precio en números "
                + "(20000000). DEBE ir vacía en el escenario B (VB-B-05).",
        },
        new("contraprestacion_descripcion")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Qué se recibe a cambio cuando el título NO es compraventa (dación, permuta). En "
                + "donación se deja vacía. Vacía también en el escenario B.",
        },
        new("forma_pago")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Cómo se paga el precio o se cumple la contraprestación. Texto libre. Vacía en el "
                + "escenario B.",
        },
        new("asume_retencion_fuente")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Opciones = TransferFiscalAssumption.ConLey,
            Guia = "Quién asume la retención en la fuente. SEGUN_LEY = lo determina la ley y no el "
                + "acuerdo de las partes. Se imprime en la cláusula SEXTA.",
        },
        new("asume_derechos_tramite")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Opciones = TransferFiscalAssumption.Derechos,
            Guia = "Quién asume los derechos de trámite. COMPARTIDOS = ambas partes por igual. Aquí NO "
                + "existe SEGUN_LEY: los derechos se pagan, y la pregunta es quién los paga.",
        },
        new("asume_impuesto_vehiculo")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Opciones = TransferFiscalAssumption.ConLey,
            Guia = "Quién asume el impuesto sobre vehículos automotores. Los remolques y semirremolques "
                + "están exentos: para ellos deja la celda vacía.",
        },
        new("ciudad_firma")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Ciudad donde se firma el documento. Se imprime en el cierre.",
        },
        new(FechaFirma)
        {
            IsDate = true,
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Fecha de firma en TEXTO con formato AAAA-MM-DD (2026-09-30). Si Excel la convierte "
                + "en fecha, la fila se rechaza con invalid_date: deja la celda en formato Texto, que es "
                + "como viene la plantilla.",
        },

        new("gravamen_activo")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Opciones = SiNo,
            Guia = "¿El vehículo tiene prenda o gravamen vigente? Es una DECLARACIÓN tuya: FLIT no "
                + "consulta el registro de garantías. Si dices SI, la columna siguiente debe decir SI o "
                + "la fila se bloquea (VB-A-04).",
        },
        new("tiene_levantamiento_o_autorizacion")
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Opciones = SiNo,
            Guia = "¿Hay levantamiento del gravamen o autorización del acreedor? Solo importa si la "
                + "columna anterior dice SI.",
        },

        new(RegimenNingunaAplica)
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Opciones = SiNo,
            Guia = "DEBE decir SI para que la fila genere. Declara que la operación no es ninguna de las "
                + "once condiciones especiales de los arts. 5.3.2.3 a 5.3.2.13. Vacía cuenta como no "
                + "respondida y bloquea igual que NO (VB-07).",
        },
        new(RegimenCondiciones)
        {
            Aplica = StandaloneBatchColumnScope.Transferencia,
            Guia = "Déjala vacía en el uso normal. Solo se llena para dejar constancia de qué condición "
                + "especial aplica, y entonces la fila NO se genera (VB-07): ese trámite exige soportes "
                + "que este documento no acredita. Varios códigos van separados por coma.",
        },

        new("transferente_es_entidad_financiera")
        {
            Aplica = StandaloneBatchColumnScope.Leasing,
            Opciones = SiNo,
            Guia = "Obligatoriamente SI en el escenario B: solo una entidad financiera puede otorgar el "
                + "acto unilateral del art. 5.3.2.2 (VB-B-01).",
        },
        new("no_contrato_leasing")
        {
            Aplica = StandaloneBatchColumnScope.Leasing,
            Guia = "Escenario B: número del contrato de leasing que origina la transferencia (VB-B-02).",
        },
        new("tipo_opcion_compra")
        {
            Aplica = StandaloneBatchColumnScope.Leasing,
            Opciones = OpcionesDeCompra,
            Guia = "Escenario B: por qué se transfiere. Con AUTOMATICA basta el contrato de leasing como "
                + "soporte (art. 5.3.2.2, Parágrafo 1.º); con EJERCIDA o TERMINACION_CONTRATO se aporta "
                + "además la declaración correspondiente.",
        },
        new(FechaTerminacion)
        {
            IsDate = true,
            Aplica = StandaloneBatchColumnScope.Leasing,
            Guia = "Escenario B: fecha de terminación del leasing o de ejercicio de la opción, en TEXTO "
                + "con formato AAAA-MM-DD. Igual que fecha_firma: si Excel la convierte en fecha, la "
                + "fila se rechaza.",
        },
        new("locatario_nombre")
        {
            Aplica = StandaloneBatchColumnScope.Leasing,
            Guia = "Escenario B: nombre o razón social del locatario, que es a quien se transfiere. En el "
                + "escenario C sirve para verificar que el adquirente NO es el locatario (VB-C-01).",
        },
        new("locatario_tipo_doc")
        {
            Aplica = StandaloneBatchColumnScope.Leasing,
            Opciones = TiposDocumento,
            Guia = "Tipo de documento del locatario.",
        },
        new("locatario_no_doc")
        {
            Aplica = StandaloneBatchColumnScope.Leasing,
            Guia = "Número de documento del locatario, sin puntos ni guiones. En el escenario C se "
                + "compara con el del adquirente (VB-C-01).",
        },
    ];

    /// <summary>Encabezados en orden, para generar el XLSX y para comparar el cargado.</summary>
    public static IReadOnlyList<string> Headers { get; } = [.. Columns.Select(c => c.Header)];

    /// <summary>
    /// <c>true</c> si el encabezado leído coincide EXACTAMENTE con el de la v1: mismo número de
    /// columnas, mismos nombres y mismo orden. La comparación ignora mayúsculas y espacios
    /// alrededor —Excel los introduce solo— pero nada más.
    /// </summary>
    public static bool HeaderMatches(IReadOnlyList<string?>? header)
    {
        if (header is null || header.Count != Headers.Count)
        {
            return false;
        }

        for (var i = 0; i < Headers.Count; i++)
        {
            if (!string.Equals(header[i]?.Trim(), Headers[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Etiqueta legible del alcance de una columna, para la hoja «Instrucciones».</summary>
    public static string ScopeLabel(StandaloneBatchColumnScope scope) => scope switch
    {
        StandaloneBatchColumnScope.Rues => "Solo Certificado RUES",
        StandaloneBatchColumnScope.Transferencia => "Solo Transferencia",
        StandaloneBatchColumnScope.Leasing => "Transferencia, escenario B",
        _ => "Las dos",
    };
}
