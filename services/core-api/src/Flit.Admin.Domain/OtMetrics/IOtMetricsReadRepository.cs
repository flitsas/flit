namespace Flit.Admin.Domain.OtMetrics;

/// <summary>
/// Lectura de los reportes del organismo de tránsito.
///
/// <para>El eje está INVERTIDO respecto a los reportes de empresa: allí un tenant mira hacia varios
/// organismos; aquí un organismo mira hacia las empresas que le radican. Por eso no puede reutilizar
/// el repositorio de analítica (que resuelve siempre un tenant) y comparte en cambio el acceso
/// cross-tenant por grant de <c>IOtClientProcedureRepository</c>.</para>
///
/// <para><paramref name="transitOfficeIdOverride"/> existe para que SuperAdmin pueda ver el reporte
/// de un organismo concreto, igual que en el resto del módulo OT.</para>
/// </summary>
public interface IOtMetricsReadRepository
{
    /// <summary>Estado ACTUAL de la cola + movimiento de hoy. Devuelve null si el tenant no resuelve organismo.</summary>
    Task<OtOperationalPanelDto?> GetOperationalPanelAsync(
        Guid otTenantId,
        OtMetricsFilter filter,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default);

    Task<OtPerformanceDto?> GetPerformanceAsync(
        Guid otTenantId,
        OtMetricsFilter filter,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default);

    Task<OtRejectionReasonsDto?> GetRejectionReasonsAsync(
        Guid otTenantId,
        OtMetricsFilter filter,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Trámites que componen un bloque del panel. Existe para que ningún número del reporte sea un
    /// callejón sin salida: quien ve «3 con más de 7 días» necesita saber cuáles son para ir a
    /// resolverlos.
    /// </summary>
    Task<OtDrilldownDto?> GetDrilldownAsync(
        Guid otTenantId,
        OtMetricsFilter filter,
        string bucket,
        int limit,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Informe del periodo: resumen por estado, tiempos y el detalle trámite a trámite.
    ///
    /// <para>El universo son los trámites que el organismo RECIBIÓ en el rango (entraron a
    /// <c>entregado</c>), no los que se decidieron. Es la única lectura que permite que el desglose
    /// por estado cierre contra el total: un trámite recibido está en exactamente un estado hoy.</para>
    /// </summary>
    Task<OtReportDto?> GetReportAsync(
        Guid otTenantId,
        OtReportQuery query,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Empresas con grant vigente con el organismo. Alimenta el filtro por empresa; se listan todas
    /// las habilitadas y no solo las que tuvieron movimiento, para que el filtro no cambie de
    /// contenido cada vez que se mueve el rango de fechas.
    /// </summary>
    Task<IReadOnlyList<OtClientCompanyOptionDto>?> ListClientCompaniesAsync(
        Guid otTenantId,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Informe de revisores: una fila por persona con su volumen, sus tiempos y su calidad.
    ///
    /// <para>El universo son las DECISIONES tomadas dentro del rango, no los trámites recibidos.
    /// Es el corte que corresponde a la pregunta —qué hizo esta persona en estas fechas— y la
    /// diferencia deliberada con el informe del periodo, cuyo universo son los recibidos.</para>
    /// </summary>
    Task<OtReviewersReportDto?> GetReviewersReportAsync(
        Guid otTenantId,
        OtReviewersQuery query,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revisores elegibles en el filtro: todos los que han decidido algo en el organismo alguna vez.
    /// Igual que el catálogo de empresas, no se recorta por rango para que el selector no cambie de
    /// contenido cada vez que se mueven las fechas.
    /// </summary>
    Task<IReadOnlyList<OtReviewerOptionDto>?> ListReviewerOptionsAsync(
        Guid otTenantId,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Foto instantánea para evaluar alertas por umbral del organismo (Reportes 2.0, HU-D — alcance
    /// OT): atascados actuales + tasa de rechazo en <paramref name="windowMinutes"/>. Devuelve null
    /// si el tenant no resuelve organismo (mismo criterio que el resto de la interfaz).
    /// </summary>
    Task<OtAlertSnapshotDto?> GetAlertSnapshotAsync(
        Guid otTenantId,
        int windowMinutes,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resuelve el tenant DUEÑO de un organismo a partir de su id de catálogo — la dirección
    /// inversa del perfil OT respecto al resto de esta interfaz (que siempre parte de un tenant).
    /// La usa SuperAdmin al programar informes/alertas de OT con
    /// <c>?transitOfficeId=</c>: esas filas se guardan con <c>tenant_id</c> (no hay columna de
    /// organismo en <c>report_schedules</c>/<c>alert_rules</c>, ver ADR de la HU), así que hace
    /// falta este paso antes de poder reutilizar el mismo CRUD que ya usa la empresa.
    /// </summary>
    Task<Guid?> ResolveTenantIdForTransitOfficeAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken = default);
}
