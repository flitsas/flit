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
    /// Empresas con grant vigente con el organismo. Alimenta el filtro por empresa; se listan todas
    /// las habilitadas y no solo las que tuvieron movimiento, para que el filtro no cambie de
    /// contenido cada vez que se mueve el rango de fechas.
    /// </summary>
    Task<IReadOnlyList<OtClientCompanyOptionDto>?> ListClientCompaniesAsync(
        Guid otTenantId,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default);
}
