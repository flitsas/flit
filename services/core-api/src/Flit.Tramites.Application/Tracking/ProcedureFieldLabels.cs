namespace Flit.Tramites.Application.Tracking;

/// <summary>
/// Catálogo de claves de <c>field_values</c> visibles al usuario y sus etiquetas en español.
///
/// <para><b>Prenda y comercial</b> no viven en <c>field_values</c>. Sus handlers arman claves
/// <c>prenda_*</c> y <c>comercial_*</c> de este catálogo al guardar.</para>
///
/// <para><b>Claves internas excluidas:</b> <c>current_step</c>, claves terminadas en <c>_runt</c>
/// (snapshot RUNT), <c>transit_office_id/actual_id/destino_id</c> (IDs internos), <c>soat_estado</c>,
/// <c>soat_vencimiento</c>, <c>valor_comercial_pendiente</c>, <c>es_leasing</c>,
/// banderas de checklist (<c>checklist_*</c>), intentos RUNT (<c>intentos_runt</c>) y
/// claves de consolidado.</para>
/// </summary>
public static class ProcedureFieldLabels
{
    // ---------------------------------------------------------------------------
    // Datos de vehículo (VehicleFieldKeys)
    // ---------------------------------------------------------------------------
    private static readonly Dictionary<string, string> _catalogo = new(StringComparer.Ordinal)
    {
        // Identificación del vehículo
        ["plate"]                       = "Placa",
        ["vin"]                         = "VIN",
        ["vehicle_brand"]               = "Marca",
        ["vehicle_line"]                = "Línea",
        ["vehicle_year"]                = "Año",
        ["vehicle_model"]               = "Año",          // alias legado de vehicle_year (Bug #11584)
        ["vehicle_class"]               = "Clase",
        ["vehicle_service"]             = "Servicio",
        ["vehicle_engine_number"]       = "Número de motor",
        ["vehicle_chassis"]             = "Chasis",
        ["vehicle_series"]              = "Serie",
        ["vehicle_engine_displacement"] = "Cilindrada",
        ["vehicle_passengers"]          = "Capacidad (pasajeros)",
        ["vehicle_axles"]               = "Número de ejes",
        ["vehicle_state"]               = "Estado del vehículo",

        // Atributos transformables — valor EFECTIVO (no snapshot RUNT)
        // Las claves *_runt son EsVisible = false (snapshot interno del RUNT).
        ["vehicle_color"]               = "Color",
        ["vehicle_fuel"]                = "Combustible",
        ["vehicle_body_type"]           = "Carrocería",

        // ---------------------------------------------------------------------------
        // Organismo de tránsito (TransitOfficeFieldKeys)
        // Solo se exponen los campos legibles; los IDs (transit_office_id, actual_id,
        // destino_id) son claves internas y EsVisible = false.
        // ---------------------------------------------------------------------------
        ["transit_office_name"]             = "Organismo de tránsito",
        ["transit_office_code"]             = "Código del organismo",
        ["transit_office_city_name"]        = "Municipio del organismo",
        ["transit_office_actual_name"]      = "Organismo actual (RUNT)",
        ["transit_office_actual_code"]      = "Código del organismo actual",
        ["transit_office_actual_city_name"] = "Municipio del organismo actual",
        ["transit_office_destino_name"]     = "Organismo destino",
        ["transit_office_destino_code"]     = "Código del organismo destino",
        ["transit_office_destino_city"]     = "Municipio del organismo destino",

        // ---------------------------------------------------------------------------
        // Transformaciones declaradas (MandatoObjetoComposer / BlindajeOpciones)
        // Estas claves son banderas booleanas ("true"/"false") o niveles (NIVEL_1, etc.)
        // que el asistente persiste cuando el tipo de trámite las declara.
        // cambio_color / cambio_carroceria / cambio_combustible: campo bool en field_values.
        // blindaje: bandera derivada (true/false). blindaje_nivel: nivel declarado.
        // ---------------------------------------------------------------------------
        ["cambio_color"]        = "Cambio de color",
        ["cambio_carroceria"]   = "Cambio de carrocería",
        ["cambio_combustible"]  = "Cambio de combustible",
        ["blindaje"]            = "Blindaje",
        ["blindaje_nivel"]      = "Nivel de blindaje",

        // Comercial (procedure_instance_commercial) y prenda (procedure_instance_prendas).
        // No son field_values: los handlers arman estas claves al guardar.
        ["comercial_valor_venta"]        = "Valor de venta",
        ["comercial_causal"]             = "Causal",
        ["comercial_tasa"]               = "Tasa de impuesto",
        ["comercial_derechos"]           = "Derechos",
        ["comercial_metodo_pago"]        = "Método de pago",
        ["prenda_decision"]              = "Decisión de prenda",
        ["prenda_acreedor"]              = "Acreedor",
        ["prenda_documento_acreedor"]    = "Documento del acreedor",
        ["prenda_entidad_levantamiento"] = "Entidad de levantamiento",
    };

    /// <summary>
    /// Devuelve <c>true</c> si la clave es un dato de formulario visible al usuario y debe
    /// registrarse en la bitácora cuando cambia su valor.
    ///
    /// <para>EsVisible = false para:
    /// <list type="bullet">
    ///   <item><c>current_step</c> — paso del asistente (no es un dato del trámite).</item>
    ///   <item>Claves terminadas en <c>_runt</c> — snapshot del RUNT, no editables por el usuario.</item>
    ///   <item>Claves de ID de organismo (<c>transit_office_id</c>, <c>transit_office_actual_id</c>,
    ///         <c>transit_office_destino_id</c>, <c>transit_office_city</c>,
    ///         <c>transit_office_actual_city</c>) — son IDs internos.</item>
    ///   <item><c>soat_estado</c>, <c>soat_vencimiento</c> — escritas por el sistema (RUNT/OCR).</item>
    ///   <item><c>valor_comercial_pendiente</c>, <c>es_leasing</c> — banderas internas.</item>
    ///   <item>Cualquier clave no presente en el catálogo.</item>
    /// </list></para>
    /// </summary>
    public static bool EsVisible(string fieldKey)
        => !string.IsNullOrEmpty(fieldKey) && _catalogo.ContainsKey(fieldKey);

    /// <summary>
    /// Etiqueta en español para la clave dada. Devuelve la clave original si no está en el
    /// catálogo (no debería llamarse con claves no visibles, pero se protege contra eso).
    /// </summary>
    public static string Etiqueta(string fieldKey)
        => _catalogo.TryGetValue(fieldKey, out var label) ? label : fieldKey;
}
