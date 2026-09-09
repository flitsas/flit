namespace Flit.Admin.Application.GeneracionDocumental.Batches;

/// <summary>
/// Una columna de la plantilla XLSX v1 (CF-11).
/// </summary>
/// <param name="Header">
/// Encabezado literal de la fila 1. Es el contrato: un encabezado distinto —cambiado de orden,
/// renombrado o con columnas de más— hace fallar la carga con <c>template_invalid</c>.
/// </param>
/// <param name="IsDate">
/// Columna de fecha. <b>Se sigue declarando como TEXTO en el XLSX</b> (formato ISO
/// <c>AAAA-MM-DD</c>): la marca solo le dice al parser que una celda NUMÉRICA aquí es un serial de
/// fecha de Excel y debe rechazarse como error de fila, en vez de intentar convertirla. Convertir
/// seriales es la vía rápida a un documento con la fecha equivocada.
/// </param>
public sealed record StandaloneBatchColumn(string Header, bool IsDate = false);

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
/// </summary>
public static class StandaloneBatchTemplate
{
    /// <summary>Versión declarada del contrato. Hoy solo existe una.</summary>
    public const string Version = "v1";

    public const string SheetName = "Lote v1";

    /// <summary>Tope duro de filas de datos (CF-11). 101 responde <c>too_many_rows</c>.</summary>
    public const int MaxRows = 100;

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
        new(DocumentType),
        new(Escenario),
        new(Nit),

        new(Placa),
        new("marca"),
        new("linea"),
        new("modelo_anio"),
        new("clase_vehiculo"),
        new("tipo_carroceria"),
        new("color"),
        new("no_motor"),
        new("no_chasis"),
        new("no_serie"),
        new("servicio"),
        new("no_licencia_transito"),
        new("organismo_transito"),

        new("transferente_tipo_persona"),
        new("transferente_nombre"),
        new("transferente_tipo_doc"),
        new("transferente_no_doc"),
        new("transferente_domicilio"),
        new("transferente_representante_legal"),
        new("transferente_cc_representante_legal"),

        new("adquirente_tipo_persona"),
        new("adquirente_nombre"),
        new("adquirente_tipo_doc"),
        new("adquirente_no_doc"),
        new("adquirente_domicilio"),
        new("adquirente_representante_legal"),
        new("adquirente_cc_representante_legal"),

        new("titulo_juridico"),
        new("descripcion_titulo"),
        new("precio_letras"),
        new("precio_numeros"),
        new("contraprestacion_descripcion"),
        new("forma_pago"),
        new("asume_retencion_fuente"),
        new("asume_derechos_tramite"),
        new("asume_impuesto_vehiculo"),
        new("ciudad_firma"),
        new(FechaFirma, IsDate: true),

        new("gravamen_activo"),
        new("tiene_levantamiento_o_autorizacion"),

        new(RegimenNingunaAplica),
        new(RegimenCondiciones),

        new("transferente_es_entidad_financiera"),
        new("no_contrato_leasing"),
        new("tipo_opcion_compra"),
        new(FechaTerminacion, IsDate: true),
        new("locatario_nombre"),
        new("locatario_tipo_doc"),
        new("locatario_no_doc"),
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
}
