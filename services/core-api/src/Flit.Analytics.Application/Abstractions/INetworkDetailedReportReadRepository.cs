using Flit.Analytics.Application.Dtos;

namespace Flit.Analytics.Application.Abstractions;

/// <summary>
/// HU #12360 (Feature #12257, épica #12235) — lectura del reporte detallado de la RED de una cabeza de
/// grupo sobre la misma vista que <see cref="IDetailedReportReadRepository"/>
/// (<c>analytics.v_procedure_detail_report</c>), con consultas NUEVAS que reciben el conjunto de clientes
/// como UN parámetro de arreglo. Es una abstracción APARTE a propósito (AC7): la firma
/// <c>DetailedReportFilter.TenantId</c> de siempre no cambia ni recibe un parámetro nuevo.
/// <para>Contrato de la implementación (AC3/AC4/AC5):</para>
/// <list type="bullet">
///   <item><c>WHERE tenant_id = ANY(@tenants)</c> con <c>@tenants</c> tipado <c>uuid[]</c>; jamás se
///   interpola un identificador en el texto SQL.</item>
///   <item>Un conjunto vacío devuelve el resultado vacío SIN ejecutar la consulta: el vacío nunca se
///   traduce en «sin filtro».</item>
///   <item>Listado y exportación comparten el mismo predicado sobre el mismo
///   <see cref="NetworkDetailedReportFilter"/>: lo que se exporta es exactamente lo que se consulta.</item>
///   <item>Cada resultado lleva los clientes DISTINTOS con filas en él
///   (<see cref="NetworkAnalyticsResult{T}.ReachedTenantIds"/>) para la auditoría de acceso (HU #12361).</item>
/// </list>
/// </summary>
public interface INetworkDetailedReportReadRepository
{
    /// <summary>
    /// Página del reporte de red. <c>ReachedTenantIds</c> son los clientes con filas en el conjunto
    /// FILTRADO completo (no solo en la página), los mismos del desglose <c>Summary.ByTenant</c>.
    /// </summary>
    Task<NetworkAnalyticsResult<NetworkDetailedProceduresPageDto>> GetNetworkProceduresAsync(
        NetworkDetailedReportFilter filter, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Recorre TODAS las filas del reporte de red (mismo predicado y orden que el listado) y devuelve
    /// los clientes distintos alcanzados.
    /// </summary>
    Task<IReadOnlyList<Guid>> ExportNetworkProceduresAsync(
        NetworkDetailedReportFilter filter,
        Func<NetworkDetailedProcedureRowDto, CancellationToken, Task> onRowAsync,
        CancellationToken ct = default);
}
