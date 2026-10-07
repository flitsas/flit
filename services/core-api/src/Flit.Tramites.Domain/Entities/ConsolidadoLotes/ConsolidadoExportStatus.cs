namespace Flit.Tramites.Domain.Entities.ConsolidadoLotes;

/// <summary>
/// HU #13367 (Feature #13306, ADR-0070) — estados de <c>tramites.consolidado_export_batches.status</c>. Deben coincidir
/// con <c>ck_consolidado_export_batches_status</c> del DDL 133 (lo vigila <c>ConsolidadoExportDdlParityTests</c>).
/// </summary>
public static class ConsolidadoExportStatus
{
    public const string EnCola = "en_cola";
    public const string EnProceso = "en_proceso";
    public const string Empaquetando = "empaquetando";
    public const string Completado = "completado";
    public const string CompletadoConOmitidos = "completado_con_omitidos";
    public const string Fallido = "fallido";
    public const string Cancelado = "cancelado";
    public const string Expirado = "expirado";

    /// <summary>
    /// Estados en los que el lote cuenta como activo: los cubre <c>uq_consolidado_export_batches_active_per_user</c>
    /// (un lote activo por usuario) y exigen <c>finished_at</c> nulo y DEK viva.
    /// </summary>
    public static readonly IReadOnlyList<string> Activos = [EnCola, EnProceso, Empaquetando];

    /// <summary>Estados terminales: exigen <c>finished_at</c> y <c>expires_at</c>.</summary>
    public static readonly IReadOnlyList<string> Terminales =
        [Completado, CompletadoConOmitidos, Fallido, Cancelado, Expirado];

    public static readonly IReadOnlyList<string> Todos = [.. Activos, .. Terminales];

    public static bool EsActivo(string status) => Activos.Contains(status);
}

/// <summary>
/// Origen del lote (<c>ck_consolidado_export_batches_origin</c>). Decide la nulabilidad de <c>tenant_id</c> (solo
/// <see cref="Superadmin"/> va sin compañía, E5) y de <c>ot_transit_office_id</c> (solo y siempre en
/// <see cref="OtBandeja"/>, R-d).
/// </summary>
public static class ConsolidadoExportOrigin
{
    public const string Tramites = "tramites";
    public const string Superadmin = "superadmin";
    public const string OtBandeja = "ot_bandeja";

    public static readonly IReadOnlyList<string> Todos = [Tramites, Superadmin, OtBandeja];
}

/// <summary>Documento que entrega el lote (<c>ck_consolidado_export_batches_document_type</c>).</summary>
public static class ConsolidadoExportDocumentType
{
    public const string Consolidado = "consolidado";
    public const string ConsolidadoMaestro = "consolidado_maestro";

    public static readonly IReadOnlyList<string> Todos = [Consolidado, ConsolidadoMaestro];
}

/// <summary>Cómo se eligieron los trámites (<c>ck_consolidado_export_batches_selection_mode</c>).</summary>
public static class ConsolidadoExportSelectionMode
{
    public const string Ids = "ids";
    public const string Filtro = "filtro";

    public static readonly IReadOnlyList<string> Todos = [Ids, Filtro];
}

/// <summary>
/// HU #13368 — estados de <c>tramites.consolidado_export_batch_parts.status</c>. Deben coincidir con
/// <c>ck_consolidado_export_batch_parts_status</c> del DDL 134 (lo vigila <c>ConsolidadoExportItemsDdlParityTests</c>).
/// </summary>
public static class ConsolidadoExportPartStatus
{
    public const string Pendiente = "pendiente";
    public const string Empaquetando = "empaquetando";
    public const string Cerrada = "cerrada";
    public const string Fallida = "fallida";
    public const string Purgada = "purgada";

    /// <summary>
    /// Parte que no llegó a cerrarse cuando se canceló el lote (#13307, D7). Terminal: no exige binario (a
    /// diferencia de <see cref="Purgada"/>) ni arrastra el lote a <c>fallido</c> (a diferencia de <see cref="Fallida"/>).
    /// </summary>
    public const string Descartada = "descartada";

    public static readonly IReadOnlyList<string> Todos = [Pendiente, Empaquetando, Cerrada, Fallida, Purgada, Descartada];
}

/// <summary>
/// HU #13368 — estados de <c>tramites.consolidado_export_batch_items.status</c>. Deben coincidir con
/// <c>ck_consolidado_export_batch_items_status</c> del DDL 134.
/// </summary>
public static class ConsolidadoExportItemStatus
{
    public const string Pendiente = "pendiente";
    public const string Procesando = "procesando";
    public const string Incluido = "incluido";
    public const string Omitido = "omitido";

    /// <summary>
    /// Ítem vivo cuando se canceló el lote (#13307, D7). Terminal: sin código de omisión, sin parte ni modo de
    /// entrega; el cierre en vuelo (<c>WHERE status = 'procesando'</c>) actualiza 0 filas.
    /// </summary>
    public const string Cancelado = "cancelado";

    /// <summary>Estados en los que el ítem aún puede reclamarse o está en proceso (sin resultado ni parte).</summary>
    public static readonly IReadOnlyList<string> Vivos = [Pendiente, Procesando];

    public static readonly IReadOnlyList<string> Todos = [Pendiente, Procesando, Incluido, Omitido, Cancelado];
}

/// <summary>Cómo se obtuvo el PDF de un ítem incluido (<c>ck_consolidado_export_batch_items_delivery_mode</c>, v2).</summary>
public static class ConsolidadoExportDeliveryMode
{
    /// <summary>Adjunto ya guardado en el trámite, tomado tal cual.</summary>
    public const string Existente = "existente";

    /// <summary>Primera generación hecha por el lote.</summary>
    public const string Generado = "generado";

    public static readonly IReadOnlyList<string> Todos = [Existente, Generado];
}

/// <summary>Eventos de <c>tramites.consolidado_export_audit.event</c> (<c>ck_consolidado_export_audit_event</c>, DDL 134).</summary>
public static class ConsolidadoExportAuditEvent
{
    public const string LoteCreado = "lote_creado";
    public const string LoteFinalizado = "lote_finalizado";
    public const string ParteDescargada = "parte_descargada";

    /// <summary>Exige total, incluidos, omitidos y generados (<c>ck_consolidado_export_audit_cancelled</c>, #13307).</summary>
    public const string LoteCancelado = "lote_cancelado";

    public const string LotePurgado = "lote_purgado";

    public static readonly IReadOnlyList<string> Todos =
        [LoteCreado, LoteFinalizado, ParteDescargada, LoteCancelado, LotePurgado];
}
