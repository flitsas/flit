namespace Flit.Tramites.Application.Auditing;

/// <summary>
/// Vocabulario de <c>tramites.network_access_audit</c> (HU #12361, Feature #12257): recursos de la
/// vista consolidada y desenlaces. Los literales están acoplados a los CHECK del DDL 113-.
/// </summary>
public static class NetworkAccessVocabulary
{
    public static class Resources
    {
        /// <summary>Listado consolidado (<c>GET /network/instances</c> y <c>POST /network/instances/search</c>).</summary>
        public const string InstancesSearch = "network.instances.search";

        /// <summary>Detalle de un trámite de un hijo (<c>GET /network/instances/{id}</c>).</summary>
        public const string InstancesDetail = "network.instances.detail";

        /// <summary>
        /// Estadísticas / conteos del universo consolidado (<c>POST /network/instances/estado-counts</c> y
        /// <c>GET /network/stats/overview</c>, HU #12359).
        /// </summary>
        public const string StatsOverview = "network.stats.overview";

        /// <summary>Top de productividad de la red (<c>GET /network/stats/productivity/top</c>, HU #12359).</summary>
        public const string StatsProductivityTop = "network.stats.productivity_top";

        /// <summary>Tendencia mensual de la red (<c>GET /network/stats/monthly-trend</c>, HU #12359).</summary>
        public const string StatsMonthlyTrend = "network.stats.monthly_trend";

        /// <summary>Reporte detallado de la red, listado paginado (<c>GET /network/reports/procedures</c>, HU #12360).</summary>
        public const string ReportsProcedures = "network.reports.procedures";

        /// <summary>Exportación a Excel del reporte detallado de la red (<c>GET /network/reports/procedures/export</c>, HU #12360).</summary>
        public const string ReportsExport = "network.reports.export";

        /// <summary>Listado de documentos de un trámite de un hijo (HU #12410).</summary>
        public const string AttachmentsList = "network.attachments.list";

        /// <summary>Descarga o visualización proxeada de un documento de un hijo (HU #12410).</summary>
        public const string AttachmentsDownload = "network.attachments.download";
    }

    public static class Results
    {
        public const string Ok = "ok";
        public const string Forbidden = "forbidden";
        public const string NotFound = "not_found";
    }
}

/// <summary>
/// Un acceso consolidado de una cabeza de red a datos de sus hijos: UNA fila por petición (AC8).
/// Solo identificadores: <paramref name="FiltersJson"/> lleva valores de filtro (estado, tipo, fechas,
/// paginación, cliente hijo), nunca placa, documento ni nombre.
/// </summary>
/// <param name="ActorUserId">Usuario de la cabeza que ejecutó la petición; <c>null</c> si no es resoluble.</param>
/// <param name="ActorTenantId">Cliente cabeza de red del actor.</param>
/// <param name="ReachedTenantIds">Hijos DISTINTOS alcanzados (no vacío; nunca la propia cabeza).</param>
/// <param name="Resource">Uno de <see cref="NetworkAccessVocabulary.Resources"/>.</param>
/// <param name="FiltersJson">Filtros aplicados serializados como JSON, o <c>null</c>.</param>
/// <param name="ProcedureId">Trámite consultado o del documento descargado; <c>null</c> en listados.</param>
/// <param name="ProcedureTenantId">Hijo dueño del trámite; <c>null</c> en listados.</param>
/// <param name="AttachmentId">Documento descargado; <c>null</c> salvo en descargas.</param>
/// <param name="Result">Uno de <see cref="NetworkAccessVocabulary.Results"/>.</param>
public sealed record NetworkAccessAuditEntry(
    Guid? ActorUserId,
    Guid ActorTenantId,
    IReadOnlyCollection<Guid> ReachedTenantIds,
    string Resource,
    string? FiltersJson,
    Guid? ProcedureId,
    Guid? ProcedureTenantId,
    Guid? AttachmentId,
    string Result);

/// <summary>
/// Escribe en <c>tramites.network_access_audit</c> (HU #12361). Igual que <c>IAdminAuditWriter</c>: la
/// implementación usa un scope/DbContext PROPIOS y es best-effort — nunca propaga excepciones ni
/// rompe la respuesta al cliente; un fallo de escritura queda en el log como advertencia.
/// </summary>
public interface INetworkAccessAuditWriter
{
    /// <summary>Registra un acceso (una fila). Ignora silenciosamente entradas sin hijos alcanzados.</summary>
    Task WriteAsync(NetworkAccessAuditEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// AC7 — registra el acceso (o el intento rechazado) a los documentos de un trámite de un hijo por
    /// la ruta proxeada de la HU #12410. Punto de entrada reutilizable: el consumidor solo aporta
    /// identificadores; <c>reached_tenant_ids = [procedureTenantId]</c>.
    /// </summary>
    /// <param name="actorUserId">Usuario de la cabeza; <c>null</c> si no es resoluble.</param>
    /// <param name="actorTenantId">Cliente cabeza de red del actor.</param>
    /// <param name="procedureTenantId">Hijo dueño del trámite.</param>
    /// <param name="procedureId">Trámite del documento.</param>
    /// <param name="attachmentId">Documento descargado; <c>null</c> para el listado de documentos.</param>
    /// <param name="resource"><see cref="NetworkAccessVocabulary.Resources.AttachmentsList"/> o <see cref="NetworkAccessVocabulary.Resources.AttachmentsDownload"/>.</param>
    /// <param name="result"><see cref="NetworkAccessVocabulary.Results.Ok"/>, <see cref="NetworkAccessVocabulary.Results.Forbidden"/> (intento rechazado) o <see cref="NetworkAccessVocabulary.Results.NotFound"/>.</param>
    Task RecordAttachmentAccessAsync(
        Guid? actorUserId,
        Guid actorTenantId,
        Guid procedureTenantId,
        Guid procedureId,
        Guid? attachmentId,
        string resource,
        string result,
        CancellationToken cancellationToken = default);
}

/// <summary>Una fila de la auditoría de accesos consolidados, sin datos personales (AC4/AC8).</summary>
public sealed record NetworkAccessAuditRow(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid? ActorUserId,
    Guid ActorTenantId,
    IReadOnlyList<Guid> ReachedTenantIds,
    string Resource,
    Guid? ProcedureId,
    Guid? ProcedureTenantId,
    Guid? AttachmentId,
    string Result);

/// <summary>
/// Criterios de consulta de la auditoría de accesos consolidados. <see cref="TenantId"/>: hijo cuyos
/// datos fueron alcanzados (dueño del trámite o presente en <c>reached_tenant_ids</c>); <c>null</c> =
/// sin acotar (solo SuperAdmin). Paginación acotada en servidor (<see cref="MaxTake"/>).
/// </summary>
public sealed record NetworkAccessAuditQuery(
    Guid? TenantId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Resource,
    int Page = 1,
    int Take = NetworkAccessAuditQuery.DefaultTake)
{
    public const int DefaultTake = 50;
    public const int MaxTake = 200;

    public int EffectivePage => Page < 1 ? 1 : Page;

    public int EffectiveTake => Take < 1 ? DefaultTake : Math.Min(Take, MaxTake);
}

/// <summary>Lee <c>tramites.network_access_audit</c> (AC2/AC4/AC7): consulta del hijo y del SuperAdmin.</summary>
public interface INetworkAccessAuditReader
{
    Task<(IReadOnlyList<NetworkAccessAuditRow> Items, int Total)> SearchAsync(
        NetworkAccessAuditQuery query,
        CancellationToken cancellationToken = default);
}
