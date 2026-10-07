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
