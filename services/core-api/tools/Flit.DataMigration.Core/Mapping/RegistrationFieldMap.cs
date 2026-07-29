namespace Flit.DataMigration.V1.Mapping;

/// <summary>
/// Diccionario columna de V1 → <c>field_key</c> de V2 para MATRÍCULA INICIAL.
///
/// <para>
/// Las 18 columnas de vehículo y organismo de tránsito son <b>idénticas</b> a las de traspaso
/// (verificado columna por columna contra <c>vehicle_registration_master</c>), así que las claves
/// destino son las mismas y un trámite migrado de cualquiera de los dos tipos se lee igual en V2.
/// </para>
///
/// <para>
/// La diferencia está en las partes: matrícula tiene <b>un solo</b> titular
/// (<c>vehicle_owner_*</c>), que en V2 se modela como actor <c>comprador</c> / entidad
/// <c>BUYER</c> — así lo hacen los trámites nativos de matrícula de V2 y así lo espera
/// <c>MatriculaGates</c>, que solo conoce un adquirente.
/// </para>
/// </summary>
public static class RegistrationFieldMap
{
    /// <summary>Mapeo directo columna V1 → field_key V2.</summary>
    public static readonly IReadOnlyDictionary<string, string> FieldKeys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Identificación del vehículo — mismas claves que traspaso.
            ["plate_complete"] = "plate",
            ["vehicle_vin_number"] = "vin",
            ["vehicle_brand"] = "vehicle_brand",
            ["vehicle_line"] = "vehicle_line",
            ["vehicle_class"] = "vehicle_class",
            ["vehicle_colors"] = "vehicle_color",
            ["vehicle_fuel_type"] = "vehicle_fuel",
            // En Colombia "modelo" = año del vehículo.
            ["vehicle_model"] = "vehicle_year",
            ["vehicle_displacement"] = "vehicle_engine_displacement",
            ["vehicle_engine_number"] = "vehicle_engine_number",
            ["vehicle_chassis_number"] = "vehicle_chassis",
            ["vehicle_serial_number"] = "vehicle_series",
            ["vehicle_bodywork_type"] = "vehicle_body_type",
            ["vehicle_capacity"] = "vehicle_passengers",
            ["vehicle_service_type"] = "vehicle_service",

            // Organismo de tránsito
            ["traffic_secretary_name"] = "transit_office_name",
            ["traffic_secretary_city"] = "transit_office_city",
            ["traffic_secretary_code"] = "transit_office_code",

            // Titular (además va como actor 'comprador')
            ["vehicle_owner_document_number"] = "owner_document_number",
            ["vehicle_owner_document_type"] = "owner_document_type",
        };

    /// <summary>
    /// Columnas consumidas por el ACTOR titular; no deben duplicarse en extras.
    /// <para>
    /// Solo las de identidad base (<c>vehicle_owner_*</c> sin sufijo) y el correo. Las variantes
    /// <c>_lr</c> (representante legal) las consume <c>BuildRepresentanteLegal</c>, y <c>_pr</c> /
    /// <c>_le</c> (tramitador / otra figura) NO se consumen hoy: se conservan en
    /// <c>legacy_v1_extras</c> a propósito, para no perderlas mientras se define su destino.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> ActorColumns =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "vehicle_owner_first_last_name", "vehicle_owner_second_last_name", "vehicle_owner_name",
            "vehicle_owner_document_type", "vehicle_owner_document_number", "vehicle_owner_address",
            "vehicle_owner_city", "vehicle_owner_phone", "vehicle_owner_phone_extension",
            "email_owner",
        };

    /// <summary>
    /// Columnas técnicas ya representadas en otra parte del modelo de V2
    /// (id → migration_map, estado → status + status_history, fechas → created_at…).
    /// </summary>
    private static readonly HashSet<string> TechnicalColumns =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "id", "created_at", "updated_at", "deleted_at",
            "process_status", "notification_status", "company_registered",
        };

    /// <summary>
    /// Columnas que NUNCA deben migrarse, ni siquiera a <c>legacy_v1_extras</c>:
    /// <c>habeas_data_request_headers_owner</c> guarda el encabezado HTTP completo de la petición
    /// original de V1, <b>incluido un token JWT Bearer</b>. Copiar tokens a V2 es una fuga de
    /// credenciales aunque estén vencidos. Mismo criterio que en traspaso.
    /// </summary>
    private static readonly HashSet<string> SensitiveColumns =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "habeas_data_request_headers_owner",
        };

    /// <summary>¿La columna contiene secretos y debe descartarse por completo?</summary>
    public static bool IsSensitive(string column) => SensitiveColumns.Contains(column);

    /// <summary>¿La columna es una referencia a un adjunto del File Manager de V1?</summary>
    public static bool IsAttachment(string column) =>
        column.StartsWith("id_attach", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// ¿La columna debe conservarse en <c>legacy_v1_extras</c>? Son las columnas con dato real que
    /// no tienen <c>field_key</c> destino ni están representadas de otra forma. Conservarlas
    /// garantiza CERO pérdida de información en la instancia 1.
    /// </summary>
    public static bool IsExtra(string column) =>
        !FieldKeys.ContainsKey(column)
        && !IsAttachment(column)
        && !IsSensitive(column)
        && !ActorColumns.Contains(column)
        && !TechnicalColumns.Contains(column);
}
