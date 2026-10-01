using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Flit.Infrastructure.Persistence.ExternalSync;
using Flit.Tramites.Domain.ExternalSync;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #13076 (Feature #13066, Épica #12737, ADR-0066) — lectura por versión de los trámites de todas
/// las compañías para el feed de sincronización externa, dentro de <see cref="ExternalSyncReadScope"/>.
/// SQL keyset parametrizado (patrón <c>AnalyticsReadRepository</c>), sin concatenar valores.
///
/// <para>Qué entra (contrato v3.1):</para>
/// <list type="bullet">
///   <item>solo trámites radicados al menos una vez: <c>EXISTS</c> en el historial con
///   <c>to_status IN ('preasignacion','entregado')</c>. Una vez dentro no salen aunque retrocedan
///   (nunca <c>draft_finalized_at</c>, que significa otra cosa);</item>
///   <item>nunca los migrados desde FLIT 1 (<c>is_migrated</c>), ni como cambio ni como borrado;</item>
///   <item>solo cambios estables: de transacciones anteriores a la más antigua en curso
///   (<c>pg_snapshot_xmin</c>: por debajo ya no puede aparecer nada nuevo) y con más de
///   <c>StabilityLag</c> de antigüedad (ventana de 5 s del contrato). La ventana CORTA la página en el
///   primer cambio aún inestable en orden de cursor, no lo salta: <c>sync_changed_at</c> es el inicio de
///   la transacción y <c>sync_xact</c> se asigna al sellar (tras esperar bloqueos), así que no van en el
///   mismo orden; filtrar fila a fila dejaría pasar una transacción posterior, el cursor la adelantaría
///   y el cambio anterior no se entregaría nunca.</item>
/// </list>
/// <para>Orden y cursor: (transacción que selló, versión). Las filas de la asignación inicial no tienen
/// transacción y cuentan como la 0.</para>
/// </summary>
internal sealed class ProcedureSyncReadRepository(FlitDbContext context) : IProcedureSyncReadRepository
{
    /// <summary>
    /// Tope del contrato (1000) más una fila: el endpoint pide una de más para saber si hay otra página
    /// (<c>hasMore</c>) sin una segunda consulta.
    /// </summary>
    public const int MaxPageSize = 1001;

    private const string Select = """
        SELECT pi.id,
               pi.tenant_id,
               COALESCE(pi.sync_xact, '0'::xid8)::text AS sync_xact,
               COALESCE(pi.sync_xact, '0'::xid8) AS sync_xact_order,
               pi.sync_version,
               pi.sync_changed_at,
               pi.deleted_at IS NOT NULL AS is_deleted
          FROM tramites.procedure_instances pi
         WHERE pi.is_migrated = false
           AND COALESCE(pi.sync_xact, '0'::xid8) < pg_snapshot_xmin(pg_current_snapshot())
           AND NOT EXISTS (SELECT 1
                             FROM tramites.procedure_instances q
                            WHERE q.sync_changed_at > clock_timestamp() - @lag
                              AND (COALESCE(q.sync_xact, '0'::xid8), q.sync_version)
                                  <= (COALESCE(pi.sync_xact, '0'::xid8), pi.sync_version))
           AND EXISTS (SELECT 1
                         FROM tramites.procedure_instance_status_history h
                        WHERE h.procedure_instance_id = pi.id
                          AND h.to_status IN ('preasignacion', 'entregado'))
        """;

    private const string OrderAndLimit = """

         ORDER BY COALESCE(pi.sync_xact, '0'::xid8), pi.sync_version
         LIMIT @limit
        """;

    private const string FromStartSql = Select + OrderAndLimit;

    private const string AfterCursorSql = Select + """

           AND (COALESCE(pi.sync_xact, '0'::xid8), pi.sync_version) > (CAST(@after_xact AS xid8), @after_version)
        """ + OrderAndLimit;

    private const string SinceSql = Select + """

           AND pi.sync_changed_at >= @since
        """ + OrderAndLimit;

    /// <summary>Claves del vehículo que se pivotan desde <c>procedure_instance_field_values</c>.</summary>
    private static readonly string[] VehicleKeys =
    [
        VehicleFieldKeys.Class, VehicleFieldKeys.Brand, VehicleFieldKeys.Line, VehicleFieldKeys.Year,
        VehicleFieldKeys.LegacyModel, VehicleFieldKeys.BodyType, VehicleFieldKeys.EngineDisplacement,
        VehicleFieldKeys.Passengers, VehicleFieldKeys.EngineNumber, VehicleFieldKeys.Series, VehicleFieldKeys.Service,
    ];

    /// <summary>
    /// HU #13079 — datos del ítem sobre la página (<c>page</c>). Compradores: TODOS los actores del rol
    /// elegido (comprador; si no hay, propietario), por ordinal (copropiedad, ADR-0053). Factura: el
    /// adjunto de tipo factura más reciente por <c>uploaded_at</c>. Aprobación: la última transición a
    /// <c>aprobado</c> (plan de la épica §5.2 y §6).
    /// </summary>
    private const string ItemsSql = """

        SELECT p.id,
               p.sync_xact,
               p.sync_version,
               p.sync_changed_at,
               p.is_deleted,
               pi.reference_number,
               pi.consecutivo,
               pi.status,
               pt.code AS tipo_codigo,
               pt.name AS tipo_nombre,
               pt.family AS tipo_familia,
               pi.created_at,
               pi.submitted_at,
               ap.fecha_aprobacion,
               pi.vin,
               pi.plate,
               v.clase, v.marca, v.linea, v.modelo_ano, v.carroceria, v.cilindraje, v.capacidad,
               v.numero_motor, v.numero_serie, v.tipo_servicio,
               tof.code AS ot_codigo,
               tof.name AS ot_nombre,
               tof.city_code AS ot_codigo_secretaria,
               tof.city_name AS ot_ciudad,
               tof.department_name AS ot_departamento,
               a.actores::text AS actores,
               f.id AS factura_adjunto_id,
               f.filename AS factura_nombre,
               f.uploaded_at AS factura_cargada_en,
               t.id AS tenant_id,
               t.tax_id AS nit,
               t.legal_name AS compania_nombre
          FROM page p
          JOIN tramites.procedure_instances pi ON pi.id = p.id
          JOIN tramites.procedure_types pt     ON pt.id = pi.procedure_type_id
          JOIN identity.tenants t              ON t.id = pi.tenant_id
          LEFT JOIN catalogs.transit_offices tof ON tof.id = pi.transit_office_id
          LEFT JOIN LATERAL (
                SELECT jsonb_agg(jsonb_build_object(
                         'ordinal', x.ordinal,
                         'porcentaje', x.ownership_percentage,
                         'actor_type', x.actor_type,
                         'person_type', x.person_type,
                         'document_type', x.document_type,
                         'document_number', x.document_number,
                         'full_name', x.full_name,
                         'direccion', x.metadata->>'direccion',
                         'ciudad', x.metadata->>'ciudad',
                         'phone', x.phone,
                         'email', x.email) ORDER BY x.ordinal) AS actores
                  FROM tramites.procedure_instance_actors x
                 WHERE x.procedure_instance_id = pi.id
                   AND x.actor_type = CASE WHEN EXISTS (SELECT 1 FROM tramites.procedure_instance_actors y
                                                         WHERE y.procedure_instance_id = pi.id AND y.actor_type = 'comprador')
                                           THEN 'comprador' ELSE 'propietario' END) a ON true
          LEFT JOIN LATERAL (
                SELECT max(h.changed_at) AS fecha_aprobacion
                  FROM tramites.procedure_instance_status_history h
                 WHERE h.procedure_instance_id = pi.id AND h.to_status = 'aprobado') ap ON true
          LEFT JOIN LATERAL (
                SELECT x.id, x.filename, x.uploaded_at
                  FROM tramites.procedure_instance_attachments x
                 WHERE x.procedure_instance_id = pi.id AND x.tipo = 'factura'
                 ORDER BY x.uploaded_at DESC, x.id DESC
                 LIMIT 1) f ON true
          LEFT JOIN LATERAL (
                SELECT max(fv.value_text) FILTER (WHERE fv.field_key = 'vehicle_class')               AS clase,
                       max(fv.value_text) FILTER (WHERE fv.field_key = 'vehicle_brand')               AS marca,
                       max(fv.value_text) FILTER (WHERE fv.field_key = 'vehicle_line')                AS linea,
                       COALESCE(NULLIF(btrim(max(fv.value_text) FILTER (WHERE fv.field_key = 'vehicle_year')), ''),
                                max(fv.value_text) FILTER (WHERE fv.field_key = 'vehicle_model'))     AS modelo_ano,
                       max(fv.value_text) FILTER (WHERE fv.field_key = 'vehicle_body_type')           AS carroceria,
                       max(fv.value_text) FILTER (WHERE fv.field_key = 'vehicle_engine_displacement') AS cilindraje,
                       max(fv.value_text) FILTER (WHERE fv.field_key = 'vehicle_passengers')          AS capacidad,
                       max(fv.value_text) FILTER (WHERE fv.field_key = 'vehicle_engine_number')       AS numero_motor,
                       max(fv.value_text) FILTER (WHERE fv.field_key = 'vehicle_series')              AS numero_serie,
                       max(fv.value_text) FILTER (WHERE fv.field_key = 'vehicle_service')             AS tipo_servicio
                  FROM tramites.procedure_instance_field_values fv
                 WHERE fv.procedure_instance_id = pi.id AND fv.field_key = ANY(@vehicle_keys)) v ON true
         ORDER BY p.sync_xact_order, p.sync_version
        """;

    /// <summary>
    /// HU #13077 — la factura de un trámite del feed. Mismo alcance que <see cref="Select"/> (radicado alguna
    /// vez, no migrado) y además sin borrado lógico: el tombstone llega con <c>factura: null</c>. Solo
    /// adjuntos de tipo factura (decisión del PO: los demás pueden llevar datos personales).
    /// </summary>
    private const string InvoiceSql = """
        SELECT x.storage_path, x.filename, x.mimetype
          FROM tramites.procedure_instance_attachments x
          JOIN tramites.procedure_instances pi ON pi.id = x.procedure_instance_id
         WHERE x.id = @attachment_id
           AND x.procedure_instance_id = @procedure_id
           AND x.tipo = 'factura'
           AND pi.is_migrated = false
           AND pi.deleted_at IS NULL
           AND EXISTS (SELECT 1
                         FROM tramites.procedure_instance_status_history h
                        WHERE h.procedure_instance_id = pi.id
                          AND h.to_status IN ('preasignacion', 'entregado'))
        """;

    public Task<IReadOnlyList<ProcedureSyncChange>> ReadChangesAsync(
        ProcedureSyncPageRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        return new ExternalSyncReadScope(context).ExecuteAsync(
            (conn, tx) => ReadChangesAsync(conn, tx, request, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// HU #13079 — la misma página que <see cref="ReadChangesAsync(ProcedureSyncPageRequest, CancellationToken)"/>
    /// con todos los datos del ítem, en UNA consulta y la misma instantánea: el ítem nunca mezcla datos
    /// de dos momentos. El catálogo de tipos de servicio se lee en la misma transacción.
    /// </summary>
    public Task<IReadOnlyList<ProcedureSyncEntry>> ReadItemsAsync(
        ProcedureSyncPageRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        return new ExternalSyncReadScope(context).ExecuteAsync(
            (conn, tx) => ReadItemsAsync(conn, tx, request, cancellationToken), cancellationToken);
    }

    public Task<ProcedureSyncInvoiceFile?> FindInvoiceAsync(
        Guid procedureId, Guid attachmentId, CancellationToken cancellationToken = default) =>
        new ExternalSyncReadScope(context).ExecuteAsync(
            (conn, tx) => FindInvoiceAsync(conn, tx, procedureId, attachmentId, cancellationToken), cancellationToken);

    private static async Task<ProcedureSyncInvoiceFile?> FindInvoiceAsync(
        DbConnection conn, DbTransaction tx, Guid procedureId, Guid attachmentId, CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = InvoiceSql;
        AddParam(cmd, "procedure_id", DbType.Guid, procedureId);
        AddParam(cmd, "attachment_id", DbType.Guid, attachmentId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new ProcedureSyncInvoiceFile(reader.GetString(0), reader.GetString(1), reader.GetString(2))
            : null;
    }

    private static void Validate(ProcedureSyncPageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.After is not null && request.Since is not null)
        {
            throw new ArgumentException("El cursor y la fecha de arranque son excluyentes.", nameof(request));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(request.PageSize, 1, nameof(request));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(request.PageSize, MaxPageSize, nameof(request));
        ArgumentOutOfRangeException.ThrowIfLessThan(request.StabilityLag, TimeSpan.Zero, nameof(request));
    }

    /// <summary>Elige la variante de la página (inicio, cursor o fecha) y fija sus parámetros.</summary>
    private static string PreparePage(DbCommand cmd, ProcedureSyncPageRequest request)
    {
        string sql;
        if (request.After is { } after)
        {
            sql = AfterCursorSql;
            AddParam(cmd, "after_xact", DbType.String, after.Transaction.ToString(CultureInfo.InvariantCulture));
            AddParam(cmd, "after_version", DbType.Int64, after.Version);
        }
        else if (request.Since is { } since)
        {
            sql = SinceSql;
            AddParam(cmd, "since", DbType.DateTimeOffset, since.ToUniversalTime());
        }
        else
        {
            sql = FromStartSql;
        }

        AddParam(cmd, "lag", DbType.Object, request.StabilityLag);
        AddParam(cmd, "limit", DbType.Int32, request.PageSize);
        return sql;
    }

    private static async Task<IReadOnlyList<ProcedureSyncChange>> ReadChangesAsync(
        DbConnection conn, DbTransaction tx, ProcedureSyncPageRequest request, CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = PreparePage(cmd, request);

        var changes = new List<ProcedureSyncChange>(request.PageSize);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            changes.Add(new ProcedureSyncChange(
                reader.GetGuid(reader.GetOrdinal("id")),
                reader.GetGuid(reader.GetOrdinal("tenant_id")),
                new ProcedureSyncPosition(
                    ulong.Parse(reader.GetString(reader.GetOrdinal("sync_xact")), CultureInfo.InvariantCulture),
                    reader.GetInt64(reader.GetOrdinal("sync_version"))),
                reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("sync_changed_at")),
                reader.GetBoolean(reader.GetOrdinal("is_deleted"))));
        }

        return changes;
    }

    private static async Task<IReadOnlyList<ProcedureSyncEntry>> ReadItemsAsync(
        DbConnection conn, DbTransaction tx, ProcedureSyncPageRequest request, CancellationToken cancellationToken)
    {
        var serviceTypeNames = await ReadServiceTypeNamesAsync(conn, tx, cancellationToken).ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "WITH page AS (" + PreparePage(cmd, request) + ")" + ItemsSql;
        AddParam(cmd, "vehicle_keys", DbType.Object, VehicleKeys);

        var entries = new List<ProcedureSyncEntry>(request.PageSize);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = ReadRow(reader);
            entries.Add(new ProcedureSyncEntry(row.Position, ProcedureSyncItemMapper.Map(row, serviceTypeNames)));
        }

        return entries;
    }

    private static async Task<Dictionary<string, string>> ReadServiceTypeNamesAsync(
        DbConnection conn, DbTransaction tx, CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT code, name FROM catalogs.vehicle_service_types";
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            names[reader.GetString(0)] = reader.GetString(1);
        }

        return names;
    }

    private static ProcedureSyncRow ReadRow(DbDataReader r) => new(
        Id: r.GetGuid(r.GetOrdinal("id")),
        Position: new ProcedureSyncPosition(
            ulong.Parse(r.GetString(r.GetOrdinal("sync_xact")), CultureInfo.InvariantCulture),
            r.GetInt64(r.GetOrdinal("sync_version"))),
        ReferenceNumber: r.GetString(r.GetOrdinal("reference_number")),
        Consecutivo: r.GetInt64(r.GetOrdinal("consecutivo")),
        ChangedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("sync_changed_at")),
        IsDeleted: r.GetBoolean(r.GetOrdinal("is_deleted")),
        Status: r.GetString(r.GetOrdinal("status")),
        ProcedureTypeCode: r.GetString(r.GetOrdinal("tipo_codigo")),
        ProcedureTypeName: r.GetString(r.GetOrdinal("tipo_nombre")),
        ProcedureTypeFamily: Str(r, "tipo_familia"),
        CreatedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")),
        SubmittedAt: Date(r, "submitted_at"),
        ApprovedAt: Date(r, "fecha_aprobacion"),
        Vin: Str(r, "vin"),
        Plate: Str(r, "plate"),
        VehicleClass: Str(r, "clase"),
        VehicleBrand: Str(r, "marca"),
        VehicleLine: Str(r, "linea"),
        VehicleYear: Str(r, "modelo_ano"),
        VehicleBodyType: Str(r, "carroceria"),
        VehicleEngineDisplacement: Str(r, "cilindraje"),
        VehiclePassengers: Str(r, "capacidad"),
        VehicleEngineNumber: Str(r, "numero_motor"),
        VehicleSeries: Str(r, "numero_serie"),
        VehicleService: Str(r, "tipo_servicio"),
        TransitOfficeCode: Str(r, "ot_codigo"),
        TransitOfficeName: Str(r, "ot_nombre"),
        TransitOfficeCityCode: Str(r, "ot_codigo_secretaria"),
        TransitOfficeCityName: Str(r, "ot_ciudad"),
        TransitOfficeDepartmentName: Str(r, "ot_departamento"),
        Actors: ReadActors(Str(r, "actores")),
        InvoiceAttachmentId: r.IsDBNull(r.GetOrdinal("factura_adjunto_id")) ? null : r.GetGuid(r.GetOrdinal("factura_adjunto_id")),
        InvoiceFilename: Str(r, "factura_nombre"),
        InvoiceUploadedAt: Date(r, "factura_cargada_en"),
        TenantId: r.GetGuid(r.GetOrdinal("tenant_id")),
        TenantTaxId: Str(r, "nit"),
        TenantLegalName: Str(r, "compania_nombre"));

    private static List<ProcedureSyncActorRow> ReadActors(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray()
            .Select(a => new ProcedureSyncActorRow(
                a.GetProperty("ordinal").GetInt32(),
                a.GetProperty("porcentaje").ValueKind == JsonValueKind.Number ? a.GetProperty("porcentaje").GetDecimal() : null,
                a.GetProperty("actor_type").GetString()!,
                JsonStr(a, "person_type"),
                JsonStr(a, "document_type"),
                JsonStr(a, "document_number"),
                JsonStr(a, "full_name"),
                JsonStr(a, "direccion"),
                JsonStr(a, "ciudad"),
                JsonStr(a, "phone"),
                JsonStr(a, "email")))
            .ToList();
    }

    private static string? JsonStr(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? Str(DbDataReader r, string column)
    {
        var i = r.GetOrdinal(column);
        return r.IsDBNull(i) ? null : r.GetString(i);
    }

    private static DateTimeOffset? Date(DbDataReader r, string column)
    {
        var i = r.GetOrdinal(column);
        return r.IsDBNull(i) ? null : r.GetFieldValue<DateTimeOffset>(i);
    }

    private static void AddParam(DbCommand cmd, string name, DbType type, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        if (type != DbType.Object)
        {
            p.DbType = type;
        }

        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
