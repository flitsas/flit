namespace Flit.Analytics.Application.Queries.SuperAdminDashboard;

// Contratos del Inicio del SuperAdmin (rediseño de los 3 dashboards, 2026-11-17). Solo los consume
// `frontend/lib/api/analytics-superadmin.ts`: NO renombrar sin coordinar con él.
//
// Vocabulario de estados (N 03, ADR-0022) y cómo se reparte en el Inicio:
//   · Completados = 'aprobado'.
//   · Rechazados  = 'rechazado' (no es final: se puede subsanar, pero hoy está en manos del gestor).
//   · En proceso  = cualquier otro estado NO final: borrador, preparado, preasignacion, asignado,
//                   entregado, subsanacion.
//   · 'anulado' y 'revocado' son finales sin resultado: cuentan en el total, en ninguna de las tres.
// Todo el universo se acota por la FECHA DE CREACIÓN del trámite, igual que /analytics/overview
// (BUG #12588: sin rango = universo completo), para que las cifras del Inicio cuadren entre sí.

/// <summary>Una fila de «Resumen de compañías activas».</summary>
/// <param name="TasaAprobacionPct">Aprobados / (aprobados + rechazados) × 100, a un decimal. <c>null</c> sin decididos.</param>
/// <param name="Represados">Trámites «en proceso» sin cambio de estado hace más de <c>StuckDays</c> días.</param>
public sealed record SuperAdminDashboardCompanyDto(
    Guid TenantId,
    string Nombre,
    int Total,
    int Activos,
    int Aprobados,
    int Rechazados,
    double? TasaAprobacionPct,
    int Represados);

/// <summary>Cuerpo de <c>GET /api/v1/analytics/superadmin/companias-resumen</c>.</summary>
public sealed record SuperAdminDashboardCompaniesDto(
    DateOnly? From,
    DateOnly? To,
    int StuckDays,
    IReadOnlyList<SuperAdminDashboardCompanyDto> Items);

/// <summary>Completados / En proceso / Rechazados de una familia («Distribución de operaciones»).</summary>
/// <param name="Categoria"><c>matriculas</c> | <c>traspasos</c> | <c>otros</c> (mismo contrato que el overview).</param>
public sealed record SuperAdminDashboardFamilyCountsDto(
    string Categoria,
    int Completados,
    int EnProceso,
    int Rechazados);

/// <summary>Una tarjeta de «Rendimiento por Organismo de Tránsito».</summary>
/// <param name="Volumen">Trámites del universo dirigidos a este organismo.</param>
/// <param name="Decididos">Decisiones (aprobado/rechazado) que entran en el promedio.</param>
/// <param name="TiempoPromedioDias">Promedio de días entre la radicación ('entregado') y la decisión. <c>null</c> sin decididos.</param>
public sealed record SuperAdminDashboardOfficeDto(
    Guid TransitOfficeId,
    string Nombre,
    int Volumen,
    int Decididos,
    double? TiempoPromedioDias,
    IReadOnlyList<SuperAdminDashboardFamilyCountsDto> Distribucion);

/// <summary>
/// Cuerpo de <c>GET /api/v1/analytics/superadmin/organismos</c>.
/// <para><see cref="TiempoPromedioDias"/> es el «Tiempo promedio ecosistema — De radicación a resolución»: el promedio
/// de TODAS las decisiones del universo, medida cada una desde la ÚLTIMA radicación ('entregado') anterior hasta la
/// decisión ('aprobado'/'rechazado'). Es la misma medida que <c>ApprovalTimesByOffice</c> de /analytics/ot-metrics, pero
/// ponderada por decisiones entre organismos (el promedio de las tarjetas pesado por su número de decididos). No suma el
/// ciclo interno (<c>InternalCycle</c>, creación → primera radicación) porque ese tramo es anterior a la radicación y
/// el rótulo dice «de radicación a resolución».</para>
/// </summary>
public sealed record SuperAdminDashboardOfficesDto(
    Guid? TenantId,
    DateOnly? From,
    DateOnly? To,
    int Decididos,
    double? TiempoPromedioDias,
    IReadOnlyList<SuperAdminDashboardOfficeDto> Items);
