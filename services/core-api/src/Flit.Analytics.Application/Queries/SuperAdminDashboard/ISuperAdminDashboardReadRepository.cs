namespace Flit.Analytics.Application.Queries.SuperAdminDashboard;

/// <summary>
/// Lectura ENTRE TENANTS del Inicio del SuperAdmin. Solo la alcanzan los endpoints con
/// <c>SuperAdminPolicy</c> (<c>SuperAdminDashboardEndpoints</c>); ningún otro rol debe llegar aquí.
/// <para><c>allowedFamilies</c> como en el resto de analítica (HU #13664): <c>null</c> = sin restricción, vacío = ninguna.</para>
/// </summary>
public interface ISuperAdminDashboardReadRepository
{
    /// <summary>
    /// Una fila por compañía ACTIVA de la plataforma (gestoras: sin perfil de organismo de tránsito, el mismo
    /// universo que las Consultas de SuperAdmin), también las que no tienen trámites en el rango.
    /// </summary>
    Task<IReadOnlyList<SuperAdminDashboardCompanyDto>> GetCompaniesAsync(
        DateOnly? fromDate, DateOnly? toDate, int stuckDays,
        IReadOnlyList<string>? allowedFamilies = null, CancellationToken ct = default);

    /// <summary>
    /// Volumen, tiempo de decisión y distribución por organismo. <paramref name="tenantId"/> <c>null</c> = todas las
    /// compañías; con valor, solo los trámites de esa compañía.
    /// </summary>
    Task<SuperAdminDashboardOfficesDto> GetOfficesAsync(
        Guid? tenantId, DateOnly? fromDate, DateOnly? toDate,
        IReadOnlyList<string>? allowedFamilies = null, CancellationToken ct = default);
}
