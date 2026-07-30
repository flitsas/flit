namespace Flit.DataMigration.V1.Mapping;

/// <summary>
/// Diccionario columna de V1 → <c>field_key</c> de V2 para TRASPASO.
/// <para>
/// Las claves destino NO son inventadas: salen del vocabulario real en uso en
/// <c>procedure_instance_field_values</c> para <c>TRASPASO_STANDARD</c>. Por eso
/// <c>vehicle_engine_number</c> y no <c>vehicle_engine</c>, y <c>vehicle_color</c>
/// (singular) aunque en V1 la columna sea <c>vehicle_colors</c>.
/// </para>
/// </summary>
public static class TransferFieldMap
{
    /// <summary>Mapeo directo columna V1 → field_key V2.</summary>
    public static readonly IReadOnlyDictionary<string, string> FieldKeys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Identificación del vehículo
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

            // Propietario actual (además va como actor 'vendedor')
            ["vehicle_owner_document_number"] = "owner_document_number",
            ["vehicle_owner_document_type"] = "owner_document_type",
        };

    /// <summary>Columnas consumidas por los ACTORES; no deben duplicarse en extras.</summary>
    private static readonly HashSet<string> ActorColumns =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "vehicle_owner_first_last_name", "vehicle_owner_second_last_name", "vehicle_owner_name",
            "vehicle_owner_document_type", "vehicle_owner_document_number", "vehicle_owner_address",
            "vehicle_owner_city", "vehicle_owner_phone", "vehicle_owner_phone_extension",
            "vehicle_buyer_first_last_name", "vehicle_buyer_second_last_name", "vehicle_buyer_name",
            "vehicle_buyer_document_type", "vehicle_buyer_document_number", "vehicle_buyer_address",
            "vehicle_buyer_city", "vehicle_buyer_phone", "vehicle_buyer_phone_extension",
            "email_seller", "email_buyer",
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
    /// Columnas que NUNCA deben migrarse, ni siquiera a <c>legacy_v1_extras</c>: contienen
    /// secretos. Detectado en la revisión: <c>habeas_data_request_headers_*</c> guarda el
    /// encabezado HTTP completo de la petición original de V1, **incluido un token JWT Bearer**.
    /// Copiar tokens de autenticación a V2 es una fuga de credenciales aunque estén vencidos.
    /// </summary>
    private static readonly HashSet<string> SensitiveColumns =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "habeas_data_request_headers_buyer",
            "habeas_data_request_headers_seller",
        };

    /// <summary>¿La columna contiene secretos y debe descartarse por completo?</summary>
    public static bool IsSensitive(string column) => SensitiveColumns.Contains(column);

    /// <summary>¿La columna es una referencia a un adjunto del File Manager de V1?</summary>
    public static bool IsAttachment(string column) =>
        column.StartsWith("id_attach", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// ¿La columna debe conservarse en <c>legacy_v1_extras</c>? Son las columnas con dato
    /// real que no tienen <c>field_key</c> destino ni están representadas de otra forma.
    /// Conservarlas garantiza CERO pérdida de información en la instancia 1, sin inventar
    /// claves nuevas en el vocabulario de V2.
    /// </summary>
    public static bool IsExtra(string column) =>
        !FieldKeys.ContainsKey(column)
        && !IsAttachment(column)
        && !IsSensitive(column)
        && !ActorColumns.Contains(column)
        && !TechnicalColumns.Contains(column);
}
