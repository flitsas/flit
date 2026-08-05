using Flit.Admin.Domain.OtMetrics;
using Flit.Infrastructure.Analytics.Scheduling;
using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Reportes del organismo de tránsito.
///
/// <para><b>Acceso.</b> Mismo mecanismo que <see cref="OtClientProcedureRepository"/>: se resuelve el
/// organismo del tenant, se listan las empresas con grant vigente y se lee bajo
/// <c>SET LOCAL row_security = off</c>. No se puede reutilizar el repositorio de analítica de
/// empresa porque aquél resuelve SIEMPRE un tenant y aquí el eje está invertido.</para>
///
/// <para><b>Por qué LINQ y agregación en memoria y no SQL crudo.</b> El repositorio de analítica de
/// empresa usa SQL multi-statement porque agrega sobre un solo tenant con RLS activo. Aquí las
/// consultas cruzan varios tenants y, sobre todo, hacen falta medianas y conteos por evento que en
/// SQL exigirían laterales que EF InMemory no soporta — y estos reportes se cubren con tests sobre
/// InMemory. Los conjuntos están acotados por organismo + rango de fechas (la cola pendiente son
/// decenas o cientos de filas, no el histórico), así que traer y agregar en memoria es un
/// intercambio razonable. Si el volumen lo pidiera, el salto natural es una vista materializada.</para>
/// </summary>
internal sealed class OtMetricsReadRepository : IOtMetricsReadRepository
{
    private static readonly TimeZoneInfo Bogota = ScheduleDueEvaluator.BogotaTimeZone;

    /// <summary>Un prioritario que lleva más de esto sin tocarse es el peor indicador del panel.</summary>
    private const int PrioritarioEstancadoDias = 3;

    private readonly FlitDbContext _context;

    public OtMetricsReadRepository(FlitDbContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    // ── A.1 Panel operativo ───────────────────────────────────────────────────────────────────

    public Task<OtOperationalPanelDto?> GetOperationalPanelAsync(
        Guid otTenantId,
        OtMetricsFilter filter,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default) =>
        ExecuteScopedAsync<OtOperationalPanelDto>(
            otTenantId,
            transitOfficeIdOverride,
            async (transitOfficeId, tenantIds) =>
            {
                var pendientes = await LoadPendingAsync(
                    transitOfficeId, tenantIds, filter, cancellationToken).ConfigureAwait(false);

                // Esperas: solo se exponen las accionables por el organismo. `asignado` (esperando
                // SOAT del cliente) y los pausados se agregan en un único número sin desglosar.
                var porRevisar = pendientes.Count(IsPorRevisar);
                var esperandoPlaca = pendientes.Count(IsEsperandoPlaca);
                var enEsperaDelCliente = pendientes.Count - porRevisar - esperandoPlaca;

                var aging = new OtAgingDto(
                    Hasta1Dia: pendientes.Count(IsHasta1Dia),
                    Entre2y3Dias: pendientes.Count(IsEntre2y3Dias),
                    Entre4y7Dias: pendientes.Count(IsEntre4y7Dias),
                    MasDe7Dias: pendientes.Count(IsMasDe7Dias),
                    PrioritariosEstancados: pendientes.Count(IsPrioritarioEstancado));

                var movimiento = await BuildDayMovementAsync(
                    transitOfficeId, tenantIds, filter, pendientes.Count, cancellationToken)
                    .ConfigureAwait(false);

                return new OtOperationalPanelDto(
                    movimiento,
                    new OtQueueBreakdownDto(porRevisar, esperandoPlaca, enEsperaDelCliente),
                    aging);
            },
            cancellationToken);

    private async Task<OtDayMovementDto> BuildDayMovementAsync(
        Guid transitOfficeId,
        IReadOnlyList<Guid> tenantIds,
        OtMetricsFilter filter,
        int pendientesTotal,
        CancellationToken cancellationToken)
    {
        // «Hoy» es el día calendario de Bogotá, no las últimas 24 horas: es como lo lee un jefe de
        // turno por la mañana.
        var todayBogota = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Bogota).DateTime);
        var (dayStart, dayEnd) = BogotaDayRange(todayBogota, todayBogota);

        var todayTransitions = await QueryTransitions(transitOfficeId, tenantIds, filter, dayStart, dayEnd)
            .Select(h => h.ToStatus)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var decisionHours = await GetDecisionHoursAsync(
            transitOfficeId, tenantIds, filter, cancellationToken).ConfigureAwait(false);

        return new OtDayMovementDto(
            EntregadosHoy: todayTransitions.Count(s => s == TramiteEstado.Entregado),
            DecididosHoy: todayTransitions.Count(IsDecision),
            PendientesTotal: pendientesTotal,
            TiempoMedianoDecisionHoras: Median(decisionHours));
    }

    // ── A.2 Desempeño ─────────────────────────────────────────────────────────────────────────

    public Task<OtPerformanceDto?> GetPerformanceAsync(
        Guid otTenantId,
        OtMetricsFilter filter,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default) =>
        ExecuteScopedAsync<OtPerformanceDto>(
            otTenantId,
            transitOfficeIdOverride,
            async (transitOfficeId, tenantIds) =>
            {
                var (from, to) = BogotaDayRange(filter.From, filter.To);

                var instanceTenant = await QueryInstances(transitOfficeId, tenantIds, filter)
                    .Select(p => new { p.Id, p.TenantId })
                    .ToDictionaryAsync(x => x.Id, x => x.TenantId, cancellationToken)
                    .ConfigureAwait(false);

                var instanceIds = instanceTenant.Keys.ToList();

                // Historial COMPLETO de esas instancias (sin recortar por rango): la reincidencia y
                // el «pasan a la primera» necesitan saber si hubo rechazos ANTES del periodo.
                var history = await _context.ProcedureInstanceStatusHistories
                    .AsNoTracking()
                    .Where(h => instanceIds.Contains(h.ProcedureInstanceId))
                    .Select(h => new HistoryRow(
                        h.ProcedureInstanceId, h.FromStatus, h.ToStatus, h.ChangedAt, h.ChangedBy))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var revisores = BuildReviewers(history, from, to);
                var displayNames = await ResolveUserNamesAsync(
                    revisores.Select(r => r.UserId).ToList(), cancellationToken).ConfigureAwait(false);

                var empresas = await BuildClientQualityAsync(
                    history, instanceTenant, from, to, cancellationToken).ConfigureAwait(false);

                return new OtPerformanceDto(
                    revisores
                        .Select(r => r with
                        {
                            DisplayName = displayNames.TryGetValue(r.UserId, out var name)
                                ? name
                                : "(usuario desconocido)",
                        })
                        .OrderByDescending(r => r.Decididos)
                        .ToList(),
                    empresas);
            },
            cancellationToken);

    private static List<OtReviewerDto> BuildReviewers(
        IReadOnlyList<HistoryRow> history,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        // Última entrega anterior a cada decisión: el reloj de la decisión arranca ahí.
        var deliveries = history
            .Where(h => h.ToStatus == TramiteEstado.Entregado)
            .GroupBy(h => h.InstanceId)
            .ToDictionary(g => g.Key, g => g.Select(h => h.ChangedAt).OrderBy(d => d).ToList());

        var rejections = history
            .Where(h => h.ToStatus == TramiteEstado.Rechazado)
            .GroupBy(h => h.InstanceId)
            .ToDictionary(g => g.Key, g => g.Select(h => h.ChangedAt).OrderBy(d => d).ToList());

        return history
            .Where(h => IsDecision(h.ToStatus)
                && h.ChangedBy is not null
                && h.ChangedAt >= from && h.ChangedAt <= to)
            .GroupBy(h => h.ChangedBy!.Value)
            .Select(g =>
            {
                var decisiones = g.ToList();
                var aprobados = decisiones.Count(d => d.ToStatus == TramiteEstado.Aprobado);
                var rechazados = decisiones.Count(d => d.ToStatus == TramiteEstado.Rechazado);

                var horas = decisiones
                    .Select(d => LastDeliveryBefore(deliveries, d))
                    .Where(h => h is not null)
                    .Select(h => h!.Value)
                    .ToList();

                // Reincidencia: de los rechazos de este revisor, cuántos volvieron a rechazarse
                // después. Es la señal de que el motivo no quedó claro la primera vez.
                var propios = decisiones.Where(d => d.ToStatus == TramiteEstado.Rechazado).ToList();
                var reincidentes = propios.Count(d =>
                    rejections.TryGetValue(d.InstanceId, out var all)
                    && all.Any(at => at > d.ChangedAt));

                return new OtReviewerDto(
                    UserId: g.Key,
                    DisplayName: string.Empty,
                    Decididos: decisiones.Count,
                    Aprobados: aprobados,
                    AprobacionPct: Pct(aprobados, decisiones.Count),
                    Rechazados: rechazados,
                    RechazoPct: Pct(rechazados, decisiones.Count),
                    TiempoMedianoHoras: Median(horas),
                    VuelvenARechazarsePct: Pct(reincidentes, propios.Count));
            })
            .ToList();
    }

    private async Task<List<OtClientCompanyQualityDto>> BuildClientQualityAsync(
        IReadOnlyList<HistoryRow> history,
        Dictionary<Guid, Guid> instanceTenant,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var rejectionCount = history
            .Where(h => h.ToStatus == TramiteEstado.Rechazado)
            .GroupBy(h => h.InstanceId)
            .ToDictionary(g => g.Key, g => g.Count());

        var entregadosPorEmpresa = new Dictionary<Guid, HashSet<Guid>>();
        var aprobadosPorEmpresa = new Dictionary<Guid, List<Guid>>();

        foreach (var h in history.Where(h => h.ChangedAt >= from && h.ChangedAt <= to))
        {
            if (!instanceTenant.TryGetValue(h.InstanceId, out var tenantId))
            {
                continue;
            }

            if (h.ToStatus == TramiteEstado.Entregado)
            {
                if (!entregadosPorEmpresa.TryGetValue(tenantId, out var set))
                {
                    entregadosPorEmpresa[tenantId] = set = [];
                }

                set.Add(h.InstanceId);
            }
            else if (h.ToStatus == TramiteEstado.Aprobado)
            {
                if (!aprobadosPorEmpresa.TryGetValue(tenantId, out var list))
                {
                    aprobadosPorEmpresa[tenantId] = list = [];
                }

                list.Add(h.InstanceId);
            }
        }

        var tenantIds = entregadosPorEmpresa.Keys.Union(aprobadosPorEmpresa.Keys).ToList();
        var names = await ResolveTenantNamesAsync(tenantIds, cancellationToken).ConfigureAwait(false);

        return tenantIds
            .Select(tenantId =>
            {
                var entregados = entregadosPorEmpresa.TryGetValue(tenantId, out var e) ? e : [];
                var aprobados = aprobadosPorEmpresa.TryGetValue(tenantId, out var a) ? a : [];

                var primeraVez = aprobados.Count(id => !rejectionCount.ContainsKey(id));
                var devoluciones = entregados.Count == 0
                    ? 0d
                    : entregados.Sum(id => rejectionCount.TryGetValue(id, out var c) ? c : 0)
                        / (double)entregados.Count;

                return new OtClientCompanyQualityDto(
                    TenantId: tenantId,
                    Name: names.TryGetValue(tenantId, out var name) ? name : "(empresa desconocida)",
                    Entregados: entregados.Count,
                    Aprobados: aprobados.Count,
                    PasanPrimeraPct: Pct(primeraVez, aprobados.Count),
                    DevolucionesPromedio: Math.Round(devoluciones, 2));
            })
            .OrderByDescending(x => x.Entregados)
            .ToList();
    }

    // ── A.3 Motivos de rechazo ────────────────────────────────────────────────────────────────

    public Task<OtRejectionReasonsDto?> GetRejectionReasonsAsync(
        Guid otTenantId,
        OtMetricsFilter filter,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default) =>
        ExecuteScopedAsync<OtRejectionReasonsDto>(
            otTenantId,
            transitOfficeIdOverride,
            async (transitOfficeId, tenantIds) =>
            {
                var (from, to) = BogotaDayRange(filter.From, filter.To);

                var instanceIds = await QueryInstances(transitOfficeId, tenantIds, filter)
                    .Select(p => p.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                // Eventos de rechazo del periodo: son el DENOMINADOR. El porcentaje se calcula sobre
                // rechazos, no sobre marcas de causal — un rechazo con tres causales sigue siendo uno.
                var rejectionEventIds = await _context.ProcedureInstanceStatusHistories
                    .AsNoTracking()
                    .Where(h => instanceIds.Contains(h.ProcedureInstanceId)
                        && h.ToStatus == TramiteEstado.Rechazado
                        && h.ChangedAt >= from && h.ChangedAt <= to)
                    .Select(h => h.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (rejectionEventIds.Count == 0)
                {
                    return new OtRejectionReasonsDto([], 0, 0, 0);
                }

                var marks = await _context.ProcedureInstanceRejectionReasons
                    .AsNoTracking()
                    .Where(r => r.StatusHistoryId != null
                        && rejectionEventIds.Contains(r.StatusHistoryId!.Value))
                    .Select(r => new { r.StatusHistoryId, r.RejectionReasonId })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var reasonIds = marks.Select(m => m.RejectionReasonId).Distinct().ToList();
                var catalog = await _context.RejectionReasons
                    .AsNoTracking()
                    .Where(r => reasonIds.Contains(r.Id))
                    .Select(r => new { r.Id, r.Code, r.Description })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var causales = marks
                    .GroupBy(m => m.RejectionReasonId)
                    .Select(g =>
                    {
                        var meta = catalog.FirstOrDefault(c => c.Id == g.Key);
                        // Rechazos DISTINTOS que la incluyen: si por lo que sea llegaran duplicados,
                        // no deben inflar el peso de la causal.
                        var rechazos = g.Select(m => m.StatusHistoryId).Distinct().Count();

                        return new OtRejectionReasonStatDto(
                            ReasonId: g.Key,
                            Code: meta?.Code ?? string.Empty,
                            Description: meta?.Description ?? "(causal retirada)",
                            Rechazos: rechazos,
                            Pct: Pct(rechazos, rejectionEventIds.Count));
                    })
                    .OrderByDescending(c => c.Rechazos)
                    .ThenBy(c => c.Description, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var eventosConCausal = marks.Select(m => m.StatusHistoryId).Distinct().Count();

                return new OtRejectionReasonsDto(
                    Causales: causales,
                    TotalRechazos: rejectionEventIds.Count,
                    // Rechazos sin ninguna causal: el hueco que hay que cerrar para que el reporte
                    // sea confiable (y lo que queda de los rechazos previos a esta funcionalidad).
                    RechazosSinCausal: rejectionEventIds.Count - eventosConCausal,
                    PromedioCausalesPorRechazo: rejectionEventIds.Count == 0
                        ? 0
                        : Math.Round(marks.Count / (double)rejectionEventIds.Count, 2));
            },
            cancellationToken);

    // ── Drill-down y catálogo de empresas ─────────────────────────────────────────────────────

    public Task<IReadOnlyList<OtClientCompanyOptionDto>?> ListClientCompaniesAsync(
        Guid otTenantId,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default) =>
        ExecuteScopedAsync<IReadOnlyList<OtClientCompanyOptionDto>>(
            otTenantId,
            transitOfficeIdOverride,
            async (_, tenantIds) =>
            {
                var names = await ResolveTenantNamesAsync(
                    tenantIds.ToList(), cancellationToken).ConfigureAwait(false);

                return names
                    .Select(kv => new OtClientCompanyOptionDto(kv.Key, kv.Value))
                    .OrderBy(c => c.Name)
                    .ToList();
            },
            cancellationToken);

    public Task<OtDrilldownDto?> GetDrilldownAsync(
        Guid otTenantId,
        OtMetricsFilter filter,
        string bucket,
        int limit,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default) =>
        ExecuteScopedAsync<OtDrilldownDto>(
            otTenantId,
            transitOfficeIdOverride,
            async (transitOfficeId, tenantIds) =>
            {
                var (ids, waiting) = await ResolveBucketIdsAsync(
                    transitOfficeId, tenantIds, filter, bucket, cancellationToken)
                    .ConfigureAwait(false);

                var total = ids.Count;

                // Los más viejos primero: en una cola, lo urgente es lo que lleva más esperando.
                var page = ids
                    .OrderByDescending(id => waiting.TryGetValue(id, out var d) ? d : 0)
                    .Take(limit)
                    .ToList();

                var rows = await QueryInstances(transitOfficeId, tenantIds, filter)
                    .Where(p => page.Contains(p.Id))
                    .Select(p => new
                    {
                        p.Id,
                        p.ReferenceNumber,
                        p.Plate,
                        p.Vin,
                        p.TenantId,
                        p.Status,
                        p.ModalidadEntrada,
                        p.Prioritario,
                    })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var names = await ResolveTenantNamesAsync(
                    rows.Select(r => r.TenantId).Distinct().ToList(), cancellationToken)
                    .ConfigureAwait(false);

                var items = rows
                    .Select(r => new OtDrilldownItemDto(
                        r.Id,
                        r.ReferenceNumber,
                        r.Plate,
                        r.Vin,
                        r.TenantId,
                        names.TryGetValue(r.TenantId, out var n) ? n : "(empresa desconocida)",
                        r.Status,
                        r.ModalidadEntrada,
                        r.Prioritario,
                        waiting.TryGetValue(r.Id, out var d) ? Math.Round(d, 1) : null))
                    .OrderByDescending(i => i.DiasEsperando ?? 0)
                    .ThenBy(i => i.ReferenceNumber)
                    .ToList();

                return new OtDrilldownDto(bucket, total, Math.Max(0, total - items.Count), items);
            },
            cancellationToken);

    /// <summary>
    /// Ids del bloque pedido. Los bloques de cola y antigüedad se resuelven con los MISMOS
    /// predicados que cuenta el panel; los de movimiento del día salen de las transiciones de hoy.
    /// </summary>
    private async Task<(List<Guid> Ids, Dictionary<Guid, double> Waiting)> ResolveBucketIdsAsync(
        Guid transitOfficeId,
        IReadOnlyList<Guid> tenantIds,
        OtMetricsFilter filter,
        string bucket,
        CancellationToken cancellationToken)
    {
        if (bucket is OtDrilldownBuckets.EntregadosHoy or OtDrilldownBuckets.DecididosHoy)
        {
            var todayBogota = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Bogota).DateTime);
            var (dayStart, dayEnd) = BogotaDayRange(todayBogota, todayBogota);

            var transitions = await QueryTransitions(
                transitOfficeId, tenantIds, filter, dayStart, dayEnd)
                .Select(h => new { h.ProcedureInstanceId, h.ToStatus })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var ids = transitions
                .Where(t => bucket == OtDrilldownBuckets.EntregadosHoy
                    ? t.ToStatus == TramiteEstado.Entregado
                    : IsDecision(t.ToStatus))
                .Select(t => t.ProcedureInstanceId)
                .Distinct()
                .ToList();

            return (ids, []);
        }

        var pendientes = await LoadPendingAsync(
            transitOfficeId, tenantIds, filter, cancellationToken).ConfigureAwait(false);

        Func<PendingRow, bool> predicate = bucket switch
        {
            OtDrilldownBuckets.PorRevisar => IsPorRevisar,
            OtDrilldownBuckets.EsperandoPlaca => IsEsperandoPlaca,
            OtDrilldownBuckets.EnEsperaDelCliente => IsEnEsperaDelCliente,
            OtDrilldownBuckets.Hasta1Dia => IsHasta1Dia,
            OtDrilldownBuckets.Entre2y3Dias => IsEntre2y3Dias,
            OtDrilldownBuckets.Entre4y7Dias => IsEntre4y7Dias,
            OtDrilldownBuckets.MasDe7Dias => IsMasDe7Dias,
            OtDrilldownBuckets.PrioritariosEstancados => IsPrioritarioEstancado,
            // OtDrilldownBuckets.Pendientes y cualquier otro conocido: la cola completa.
            _ => _ => true,
        };

        var matched = pendientes.Where(predicate).ToList();

        return (
            matched.Select(p => p.Id).ToList(),
            matched.ToDictionary(p => p.Id, p => p.DaysWaiting));
    }

    // ── Pendientes: una sola definición para el panel y para el drill-down ────────────────────

    /// <summary>
    /// Un trámite pendiente con su antigüedad ya resuelta. El panel cuenta sobre esta lista y el
    /// drill-down filtra sobre ella con los mismos predicados: así el número de la tarjeta y las
    /// filas que se abren no pueden divergir.
    /// </summary>
    private sealed record PendingRow(
        Guid Id,
        string? PlateFlowStatus,
        bool Prioritario,
        bool IsPaused,
        double DaysWaiting);

    private static bool IsPorRevisar(PendingRow p) => p.PlateFlowStatus is null && !p.IsPaused;

    private static bool IsEsperandoPlaca(PendingRow p) =>
        p.PlateFlowStatus == PlateFlowStatus.Preasignado && !p.IsPaused;

    private static bool IsEnEsperaDelCliente(PendingRow p) =>
        !IsPorRevisar(p) && !IsEsperandoPlaca(p);

    private static bool IsHasta1Dia(PendingRow p) => p.DaysWaiting <= 1;

    private static bool IsEntre2y3Dias(PendingRow p) => p.DaysWaiting > 1 && p.DaysWaiting <= 3;

    private static bool IsEntre4y7Dias(PendingRow p) => p.DaysWaiting > 3 && p.DaysWaiting <= 7;

    private static bool IsMasDe7Dias(PendingRow p) => p.DaysWaiting > 7;

    private static bool IsPrioritarioEstancado(PendingRow p) =>
        p.Prioritario && p.DaysWaiting > PrioritarioEstancadoDias;

    private async Task<List<PendingRow>> LoadPendingAsync(
        Guid transitOfficeId,
        IReadOnlyList<Guid> tenantIds,
        OtMetricsFilter filter,
        CancellationToken cancellationToken)
    {
        var pendientes = await QueryInstances(transitOfficeId, tenantIds, filter)
            .Where(p => p.Status == TramiteEstado.Entregado)
            .Select(p => new
            {
                p.Id,
                p.PlateFlowStatus,
                p.Prioritario,
                p.IsPaused,
                p.CreatedAt,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var pendingIds = pendientes.Select(p => p.Id).ToList();

        // Momento en que cada pendiente entró a 'entregado' — de ahí sale la antigüedad.
        var deliveredAt = await _context.ProcedureInstanceStatusHistories
            .AsNoTracking()
            .Where(h => pendingIds.Contains(h.ProcedureInstanceId)
                && h.ToStatus == TramiteEstado.Entregado)
            .GroupBy(h => h.ProcedureInstanceId)
            .Select(g => new { InstanceId = g.Key, At = g.Max(h => h.ChangedAt) })
            .ToDictionaryAsync(x => x.InstanceId, x => x.At, cancellationToken)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;

        return pendientes
            .Select(p => new PendingRow(
                p.Id,
                p.PlateFlowStatus,
                p.Prioritario,
                p.IsPaused,
                (now - (deliveredAt.TryGetValue(p.Id, out var at) ? at : p.CreatedAt)).TotalDays))
            .ToList();
    }

    // ── Consultas base ────────────────────────────────────────────────────────────────────────

    private IQueryable<Flit.Tramites.Domain.Entities.ProcedureInstance> QueryInstances(
        Guid transitOfficeId,
        IReadOnlyList<Guid> tenantIds,
        OtMetricsFilter filter)
    {
        var query = _context.ProcedureInstances
            .AsNoTracking()
            .Where(p => p.DeletedAt == null
                && p.TransitOfficeId == transitOfficeId
                && tenantIds.Contains(p.TenantId));

        if (!string.IsNullOrWhiteSpace(filter.Modalidad))
        {
            query = query.Where(p => p.ModalidadEntrada == filter.Modalidad);
        }

        if (filter.ClientTenantId is Guid clientTenantId && clientTenantId != Guid.Empty)
        {
            query = query.Where(p => p.TenantId == clientTenantId);
        }

        return query;
    }

    private IQueryable<Flit.Tramites.Domain.Entities.ProcedureInstanceStatusHistory> QueryTransitions(
        Guid transitOfficeId,
        IReadOnlyList<Guid> tenantIds,
        OtMetricsFilter filter,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var instances = QueryInstances(transitOfficeId, tenantIds, filter).Select(p => p.Id);

        return _context.ProcedureInstanceStatusHistories
            .AsNoTracking()
            .Where(h => instances.Contains(h.ProcedureInstanceId)
                && h.ChangedAt >= from && h.ChangedAt <= to);
    }

    /// <summary>Horas entre la última entrega y cada decisión tomada dentro del rango.</summary>
    private async Task<List<double>> GetDecisionHoursAsync(
        Guid transitOfficeId,
        IReadOnlyList<Guid> tenantIds,
        OtMetricsFilter filter,
        CancellationToken cancellationToken)
    {
        var (from, to) = BogotaDayRange(filter.From, filter.To);

        var instanceIds = await QueryInstances(transitOfficeId, tenantIds, filter)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var history = await _context.ProcedureInstanceStatusHistories
            .AsNoTracking()
            .Where(h => instanceIds.Contains(h.ProcedureInstanceId))
            .Select(h => new HistoryRow(
                h.ProcedureInstanceId, h.FromStatus, h.ToStatus, h.ChangedAt, h.ChangedBy))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var deliveries = history
            .Where(h => h.ToStatus == TramiteEstado.Entregado)
            .GroupBy(h => h.InstanceId)
            .ToDictionary(g => g.Key, g => g.Select(h => h.ChangedAt).OrderBy(d => d).ToList());

        return history
            .Where(h => IsDecision(h.ToStatus) && h.ChangedAt >= from && h.ChangedAt <= to)
            .Select(h => LastDeliveryBefore(deliveries, h))
            .Where(h => h is not null)
            .Select(h => h!.Value)
            .ToList();
    }

    private async Task<Dictionary<Guid, string>> ResolveUserNamesAsync(
        List<Guid> userIds,
        CancellationToken cancellationToken) =>
        userIds.Count == 0
            ? []
            : await _context.Users
                .AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken)
                .ConfigureAwait(false);

    private async Task<Dictionary<Guid, string>> ResolveTenantNamesAsync(
        List<Guid> tenantIds,
        CancellationToken cancellationToken) =>
        tenantIds.Count == 0
            ? []
            : await _context.Tenants
                .AsNoTracking()
                .Where(t => tenantIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.LegalName, cancellationToken)
                .ConfigureAwait(false);

    // ── Scope OT (grant + organismo) ──────────────────────────────────────────────────────────

    private async Task<T?> ExecuteScopedAsync<T>(
        Guid otTenantId,
        Guid? transitOfficeIdOverride,
        Func<Guid, IReadOnlyList<Guid>, Task<T>> action,
        CancellationToken cancellationToken)
        where T : class
    {
        var transitOfficeId = transitOfficeIdOverride is Guid overrideId && overrideId != Guid.Empty
            ? overrideId
            : await ResolveTransitOfficeIdAsync(otTenantId, cancellationToken).ConfigureAwait(false);

        if (transitOfficeId is null)
        {
            return null;
        }

        return await ExecuteCrossTenantReadAsync(
            async () =>
            {
                var tenantIds = await _context.TenantTransitOfficeGrants
                    .AsNoTracking()
                    .Where(g => g.TransitOfficeId == transitOfficeId.Value && g.IsEnabled)
                    .Select(g => g.TenantId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                return await action(transitOfficeId.Value, tenantIds).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Guid?> ResolveTransitOfficeIdAsync(
        Guid otTenantId,
        CancellationToken cancellationToken)
    {
        var profile = await ExecuteCrossTenantReadAsync(
            () => _context.TransitOfficeProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.TenantId == otTenantId, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        return profile?.TransitOfficeId;
    }

    private async Task<T> ExecuteCrossTenantReadAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (!_context.Database.IsRelational())
        {
            return await action().ConfigureAwait(false);
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var transaction = await _context.Database
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                await _context.Database.ExecuteSqlRawAsync(
                    "SET LOCAL row_security = off", cancellationToken).ConfigureAwait(false);

                var result = await action().ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
        }).ConfigureAwait(false);
    }

    // ── Utilidades ────────────────────────────────────────────────────────────────────────────

    private sealed record HistoryRow(
        Guid InstanceId,
        string? FromStatus,
        string ToStatus,
        DateTimeOffset ChangedAt,
        Guid? ChangedBy);

    private static bool IsDecision(string status) =>
        status == TramiteEstado.Aprobado || status == TramiteEstado.Rechazado;

    private static double? LastDeliveryBefore(
        Dictionary<Guid, List<DateTimeOffset>> deliveries,
        HistoryRow decision)
    {
        if (!deliveries.TryGetValue(decision.InstanceId, out var entregas))
        {
            return null;
        }

        DateTimeOffset? last = null;
        foreach (var at in entregas)
        {
            if (at <= decision.ChangedAt)
            {
                last = at;
            }
        }

        return last is null ? null : (decision.ChangedAt - last.Value).TotalHours;
    }

    /// <summary>Mediana (p50). Devuelve null sin datos: preferible a un 0 que se lee como «instantáneo».</summary>
    private static double? Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var ordered = values.OrderBy(v => v).ToList();
        var mid = ordered.Count / 2;

        return Math.Round(
            ordered.Count % 2 == 1 ? ordered[mid] : (ordered[mid - 1] + ordered[mid]) / 2,
            2);
    }

    private static double Pct(int part, int total) =>
        total == 0 ? 0 : Math.Round(part * 100.0 / total, 1);

    /// <summary>
    /// Convierte un rango de días de negocio (Bogotá) al intervalo UTC correspondiente. El día
    /// «hasta» se incluye entero.
    ///
    /// <para>El resultado se normaliza a UTC con <see cref="DateTimeOffset.ToUniversalTime"/>: son
    /// el mismo instante, pero Npgsql rechaza escribir un <c>DateTimeOffset</c> con offset distinto
    /// de cero en una columna <c>timestamptz</c>, y estos valores viajan como parámetros de consulta.
    /// Sin esto la consulta revienta contra PostgreSQL aunque funcione sobre InMemory.</para>
    /// </summary>
    private static (DateTimeOffset From, DateTimeOffset To) BogotaDayRange(DateOnly from, DateOnly to)
    {
        var offset = Bogota.GetUtcOffset(DateTime.SpecifyKind(
            from.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified));

        return (
            new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), offset).ToUniversalTime(),
            new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), offset).ToUniversalTime());
    }
}
