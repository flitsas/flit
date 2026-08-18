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
    private readonly OtTenantScope _scope;

    public OtMetricsReadRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _scope = new OtTenantScope(_context);
    }

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

    // ── B. Informe del periodo ────────────────────────────────────────────────────────────────

    public Task<OtReportDto?> GetReportAsync(
        Guid otTenantId,
        OtReportQuery query,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default) =>
        ExecuteScopedAsync<OtReportDto>(
            otTenantId,
            transitOfficeIdOverride,
            async (transitOfficeId, tenantIds) =>
            {
                var filter = query.Filter;
                var (from, to) = BogotaDayRange(filter.From, filter.To);

                var instances = await QueryInstances(transitOfficeId, tenantIds, filter)
                    .Select(p => new ReportInstanceRow(
                        p.Id,
                        p.ReferenceNumber,
                        p.Plate,
                        p.Vin,
                        p.TenantId,
                        p.ModalidadEntrada,
                        p.Status,
                        p.PlateFlowStatus,
                        p.Prioritario,
                        p.SubsanacionActiva,
                        p.IsPaused))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var instanceIds = instances.Select(i => i.Id).ToList();

                // Historial COMPLETO, no solo el del rango: la fila necesita saber si la decisión
                // cayó después del periodo y cuántas devoluciones acumuló desde que se radicó.
                var history = await _context.ProcedureInstanceStatusHistories
                    .AsNoTracking()
                    .Where(h => instanceIds.Contains(h.ProcedureInstanceId))
                    .Select(h => new HistoryEventRow(
                        h.Id, h.ProcedureInstanceId, h.ToStatus, h.ChangedAt, h.ChangedBy))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var byInstance = history
                    .GroupBy(h => h.InstanceId)
                    .ToDictionary(g => g.Key, g => g.OrderBy(h => h.ChangedAt).ToList());

                var rows = BuildReportRows(instances, byInstance, from, to);

                var names = await ResolveTenantNamesAsync(
                    rows.Select(r => r.Instance.TenantId).Distinct().ToList(), cancellationToken)
                    .ConfigureAwait(false);

                var resumen = BuildReportSummary(rows, filter, from, to);

                var page = Math.Max(1, query.Page);
                var pageSize = Math.Clamp(query.PageSize, 1, OtReportLimits.MaxPageSize);

                var ordered = SortReportRows(rows, names, query.SortBy, query.Descending)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                // Causales y nombres de revisor se resuelven solo para la página visible: son joins
                // extra por fila y traerlos del universo entero para mostrar cincuenta sería
                // trabajo tirado.
                var causales = await ResolveLastRejectionReasonsAsync(ordered, cancellationToken)
                    .ConfigureAwait(false);

                var revisores = await ResolveUserNamesAsync(
                    ordered
                        .Select(r => r.DecididoPor)
                        .Where(id => id is not null)
                        .Select(id => id!.Value)
                        .Distinct()
                        .ToList(),
                    cancellationToken)
                    .ConfigureAwait(false);

                var filas = ordered
                    .Select(r => ToReportRowDto(r, names, causales, revisores))
                    .ToList();

                return new OtReportDto(resumen, rows.Count, page, pageSize, filas);
            },
            cancellationToken);

    /// <summary>Datos de la instancia que el informe necesita, ya materializados.</summary>
    private sealed record ReportInstanceRow(
        Guid Id,
        string ReferenceNumber,
        string? Plate,
        string? Vin,
        Guid TenantId,
        string ModalidadEntrada,
        string Status,
        string? PlateFlowStatus,
        bool Prioritario,
        bool SubsanacionActiva,
        bool IsPaused);

    private sealed record HistoryEventRow(
        Guid Id,
        Guid InstanceId,
        string ToStatus,
        DateTimeOffset ChangedAt,
        Guid? ChangedBy);

    /// <summary>Una fila del informe con lo ya calculado, antes de resolver nombres y causales.</summary>
    private sealed record ReportRow(
        ReportInstanceRow Instance,
        string EstadoOt,
        DateTimeOffset RadicadoEn,
        DateTimeOffset? UltimaRadicacionEn,
        DateTimeOffset? DecididoEn,
        string? DecisionStatus,
        Guid? DecididoPor,
        Guid? UltimoRechazoEventId,
        double? HorasHastaDecision,
        double? DiasEnOrganismo,
        int Devoluciones);

    /// <summary>
    /// Universo del informe: los trámites que ENTRARON a <c>entregado</c> dentro del rango. Un
    /// trámite radicado antes del periodo y decidido dentro NO cuenta: el informe responde «qué
    /// recibí y en qué acabó», y mezclarlo con lo que solo se decidió haría que el desglose por
    /// estado dejara de cerrar contra el total.
    /// </summary>
    private static List<ReportRow> BuildReportRows(
        IReadOnlyList<ReportInstanceRow> instances,
        Dictionary<Guid, List<HistoryEventRow>> byInstance,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = new List<ReportRow>();

        foreach (var instance in instances)
        {
            if (!byInstance.TryGetValue(instance.Id, out var events))
            {
                continue;
            }

            var radicacion = events.FirstOrDefault(e =>
                e.ToStatus == TramiteEstado.Entregado && e.ChangedAt >= from && e.ChangedAt <= to);

            if (radicacion is null)
            {
                continue;
            }

            var posteriores = events.Where(e => e.ChangedAt >= radicacion.ChangedAt).ToList();

            var decision = posteriores.LastOrDefault(e => IsDecision(e.ToStatus));
            var ultimaRadicacion = posteriores
                .LastOrDefault(e => e.ToStatus == TramiteEstado.Entregado
                    && (decision is null || e.ChangedAt <= decision.ChangedAt));

            // El reloj arranca en la ÚLTIMA radicación previa a la decisión: es el turno que el
            // organismo trabajó. Desde la primera sumaría el tiempo que el gestor tardó en subsanar.
            var horas = decision is not null && ultimaRadicacion is not null
                ? Math.Round((decision.ChangedAt - ultimaRadicacion.ChangedAt).TotalHours, 2)
                : (double?)null;

            var cierre = decision?.ChangedAt ?? now;

            rows.Add(new ReportRow(
                Instance: instance,
                EstadoOt: ResolveReportEstado(instance),
                RadicadoEn: radicacion.ChangedAt,
                UltimaRadicacionEn: ultimaRadicacion?.ChangedAt,
                DecididoEn: decision?.ChangedAt,
                DecisionStatus: decision?.ToStatus,
                DecididoPor: decision?.ChangedBy,
                UltimoRechazoEventId: posteriores
                    .LastOrDefault(e => e.ToStatus == TramiteEstado.Rechazado)?.Id,
                HorasHastaDecision: horas,
                DiasEnOrganismo: Math.Round((cierre - radicacion.ChangedAt).TotalDays, 2),
                Devoluciones: posteriores.Count(e => e.ToStatus == TramiteEstado.Rechazado)));
        }

        return rows;
    }

    /// <summary>
    /// Estado del trámite LEÍDO DESDE EL ORGANISMO. Los buckets son excluyentes y exhaustivos: cada
    /// trámite del universo cae en exactamente uno, y por eso el desglose del informe suma el total.
    /// </summary>
    private static string ResolveReportEstado(ReportInstanceRow instance) =>
        OtEstadoResolver.Resolve(
            instance.Status, instance.SubsanacionActiva, instance.IsPaused, instance.PlateFlowStatus);

    private static OtReportSummaryDto BuildReportSummary(
        IReadOnlyList<ReportRow> rows,
        OtMetricsFilter filter,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var horas = rows
            .Where(r => r.HorasHastaDecision is not null)
            .Select(r => r.HorasHastaDecision!.Value)
            .ToList();

        var horasAprobacion = rows
            .Where(r => r.DecisionStatus == TramiteEstado.Aprobado && r.HorasHastaDecision is not null)
            .Select(r => r.HorasHastaDecision!.Value)
            .ToList();

        var devoluciones = rows.Sum(r => r.Devoluciones);

        return new OtReportSummaryDto(
            Total: rows.Count,
            EnRevision: rows.Count(r => r.EstadoOt == OtReportEstado.EnRevision),
            EsperandoPlaca: rows.Count(r => r.EstadoOt == OtReportEstado.EsperandoPlaca),
            EsperandoCliente: rows.Count(r => r.EstadoOt == OtReportEstado.EsperandoCliente),
            Aprobados: rows.Count(r => r.EstadoOt == OtReportEstado.Aprobado),
            EnSubsanacion: rows.Count(r => r.EstadoOt == OtReportEstado.EnSubsanacion),
            Rechazados: rows.Count(r => r.EstadoOt == OtReportEstado.Rechazado),
            Anulados: rows.Count(r => r.EstadoOt == OtReportEstado.Anulado),
            Otros: rows.Count(r => r.EstadoOt == OtReportEstado.Otro),
            Decididos: rows.Count(r => r.DecididoEn is not null),
            Devoluciones: devoluciones,
            DevolucionesPromedio: rows.Count == 0
                ? 0
                : Math.Round(devoluciones / (double)rows.Count, 2),
            TiempoMedianoHoras: Median(horas),
            TiempoPromedioHoras: horas.Count == 0 ? null : Math.Round(horas.Average(), 2),
            TiempoP90Horas: Percentile(horas, 0.9),
            TiempoMedianoAprobacionHoras: Median(horasAprobacion),
            DistribucionTiempos: BuildTimeBuckets(horas),
            Granularidad: ResolveGranularity(filter.From, filter.To),
            Serie: BuildReportSeries(rows, filter, from, to));
    }

    /// <summary>
    /// Histograma de tiempos de decisión. Los tramos están puestos donde el organismo toma
    /// decisiones distintas: dentro del día es rutina, más de una semana ya es un caso a explicar.
    /// </summary>
    private static List<OtReportTimeBucketDto> BuildTimeBuckets(IReadOnlyList<double> horas) =>
    [
        new("h_0_24", "Menos de 1 día", horas.Count(h => h < 24)),
        new("d_1_2", "1 a 2 días", horas.Count(h => h >= 24 && h < 72)),
        new("d_3_5", "3 a 5 días", horas.Count(h => h >= 72 && h < 144)),
        new("d_6_10", "6 a 10 días", horas.Count(h => h >= 144 && h < 264)),
        new("d_mas_10", "Más de 10 días", horas.Count(h => h >= 264)),
    ];

    /// <summary>
    /// Granularidad de la serie según el ancho del rango. Un rango de un año agrupado por día son
    /// 365 puntos ilegibles; uno de una semana agrupado por mes es un solo punto, que no es una
    /// tendencia sino un número disfrazado de gráfica.
    /// </summary>
    private static string ResolveGranularity(DateOnly from, DateOnly to)
    {
        var dias = to.DayNumber - from.DayNumber + 1;
        return dias switch
        {
            <= 31 => OtReportGranularity.Dia,
            <= 120 => OtReportGranularity.Semana,
            _ => OtReportGranularity.Mes,
        };
    }

    /// <summary>
    /// Serie de radicados y decisiones a lo largo del periodo, con TODOS los periodos presentes
    /// aunque estén en cero. Emitir solo los que tienen actividad produce gráficas que mienten: los
    /// huecos se leen como continuidad.
    /// </summary>
    private static List<OtReportSeriesPointDto> BuildReportSeries(
        IReadOnlyList<ReportRow> rows,
        OtMetricsFilter filter,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var granularidad = ResolveGranularity(filter.From, filter.To);

        var radicados = new Dictionary<string, int>();
        var aprobados = new Dictionary<string, int>();
        var rechazados = new Dictionary<string, int>();

        foreach (var row in rows)
        {
            Increment(radicados, SeriesBucketKey(row.RadicadoEn, granularidad));

            // Las decisiones fuera del rango no entran en la serie: la gráfica cubre el periodo
            // pedido, y un aprobado del mes siguiente no tiene columna donde caer.
            if (row.DecididoEn is not DateTimeOffset decidido
                || decidido < from || decidido > to)
            {
                continue;
            }

            var key = SeriesBucketKey(decidido, granularidad);
            if (row.DecisionStatus == TramiteEstado.Aprobado)
            {
                Increment(aprobados, key);
            }
            else if (row.DecisionStatus == TramiteEstado.Rechazado)
            {
                Increment(rechazados, key);
            }
        }

        return EnumerateBuckets(filter.From, filter.To, granularidad)
            .Select(b => new OtReportSeriesPointDto(
                b.Key,
                b.Label,
                b.Desde.ToString("yyyy-MM-dd"),
                b.Hasta.ToString("yyyy-MM-dd"),
                radicados.GetValueOrDefault(b.Key),
                aprobados.GetValueOrDefault(b.Key),
                rechazados.GetValueOrDefault(b.Key)))
            .ToList();

        static void Increment(Dictionary<string, int> counter, string key) =>
            counter[key] = counter.GetValueOrDefault(key) + 1;
    }

    /// <summary>Bucket de un instante, en día calendario de Bogotá — el mismo huso con el que se lee el reporte.</summary>
    private static string SeriesBucketKey(DateTimeOffset at, string granularidad)
    {
        var local = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, Bogota).DateTime);
        return BucketKeyOf(local, granularidad);
    }

    private static string BucketKeyOf(DateOnly date, string granularidad) => granularidad switch
    {
        OtReportGranularity.Mes => $"{date.Year:D4}-{date.Month:D2}",
        OtReportGranularity.Semana => StartOfWeek(date).ToString("yyyy-MM-dd"),
        _ => date.ToString("yyyy-MM-dd"),
    };

    /// <summary>Lunes de la semana. Las semanas del reporte empiezan en lunes, como la semana laboral.</summary>
    private static DateOnly StartOfWeek(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>
    /// Periodos del eje, con sus límites RECORTADOS contra el rango pedido.
    /// <para>El recorte importa porque estos límites se usan para acotar el informe al pinchar la
    /// columna: si el primer mes empezara el día 1 cuando el rango arranca el 20, el usuario haría
    /// clic en una barra de 4 trámites y aterrizaría en un informe con 30.</para>
    /// </summary>
    private static IEnumerable<(string Key, string Label, DateOnly Desde, DateOnly Hasta)> EnumerateBuckets(
        DateOnly from,
        DateOnly to,
        string granularidad)
    {
        var seen = new HashSet<string>();

        if (granularidad == OtReportGranularity.Mes)
        {
            for (var cursor = new DateOnly(from.Year, from.Month, 1);
                cursor <= to;
                cursor = cursor.AddMonths(1))
            {
                var key = BucketKeyOf(cursor, granularidad);
                if (seen.Add(key))
                {
                    yield return (
                        key,
                        $"{MesCorto(cursor.Month)} {cursor.Year}",
                        Max(cursor, from),
                        Min(cursor.AddMonths(1).AddDays(-1), to));
                }
            }

            yield break;
        }

        var span = granularidad == OtReportGranularity.Semana ? 7 : 1;
        var start = granularidad == OtReportGranularity.Semana ? StartOfWeek(from) : from;

        for (var cursor = start; cursor <= to; cursor = cursor.AddDays(span))
        {
            var key = BucketKeyOf(cursor, granularidad);
            if (seen.Add(key))
            {
                yield return (
                    key,
                    $"{cursor.Day:D2} {MesCorto(cursor.Month)}",
                    Max(cursor, from),
                    Min(cursor.AddDays(span - 1), to));
            }
        }
    }

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    /// <summary>
    /// Mes abreviado en español, fijo. Se evita <c>ToString("MMM")</c> a propósito: dependería de la
    /// cultura del proceso y el reporte cambiaría de idioma según dónde corra el servidor.
    /// </summary>
    private static string MesCorto(int month) => month switch
    {
        1 => "ene", 2 => "feb", 3 => "mar", 4 => "abr", 5 => "may", 6 => "jun",
        7 => "jul", 8 => "ago", 9 => "sep", 10 => "oct", 11 => "nov", _ => "dic",
    };

    private static IEnumerable<ReportRow> SortReportRows(
        IReadOnlyList<ReportRow> rows,
        IReadOnlyDictionary<Guid, string> names,
        string? sortBy,
        bool descending)
    {
        // El desempate por referencia no es cosmético: sin él dos filas con la misma fecha pueden
        // cambiar de orden entre páginas y el usuario ve un trámite repetido o ninguno.
        IOrderedEnumerable<ReportRow> ordered = sortBy switch
        {
            OtReportSort.Decidido => Apply(rows, r => r.DecididoEn ?? DateTimeOffset.MinValue),
            OtReportSort.Dias => Apply(rows, r => r.DiasEnOrganismo ?? 0),
            OtReportSort.Empresa => Apply(
                rows, r => names.GetValueOrDefault(r.Instance.TenantId, string.Empty)),
            OtReportSort.Referencia => Apply(rows, r => r.Instance.ReferenceNumber),
            OtReportSort.Devoluciones => Apply(rows, r => r.Devoluciones),
            OtReportSort.Estado => Apply(rows, r => r.EstadoOt),
            _ => Apply(rows, r => r.RadicadoEn),
        };

        return ordered.ThenBy(r => r.Instance.ReferenceNumber, StringComparer.OrdinalIgnoreCase);

        IOrderedEnumerable<ReportRow> Apply<TKey>(
            IEnumerable<ReportRow> source,
            Func<ReportRow, TKey> selector) =>
            descending ? source.OrderByDescending(selector) : source.OrderBy(selector);
    }

    /// <summary>
    /// Causales del último rechazo de cada fila de la página. Se resuelven contra el catálogo, y una
    /// causal retirada se nombra como tal en vez de desaparecer: el rechazo histórico sí ocurrió.
    /// </summary>
    private async Task<Dictionary<Guid, List<string>>> ResolveLastRejectionReasonsAsync(
        IReadOnlyList<ReportRow> rows,
        CancellationToken cancellationToken)
    {
        var eventIds = rows
            .Select(r => r.UltimoRechazoEventId)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (eventIds.Count == 0)
        {
            return [];
        }

        var marks = await _context.ProcedureInstanceRejectionReasons
            .AsNoTracking()
            .Where(r => r.StatusHistoryId != null && eventIds.Contains(r.StatusHistoryId!.Value))
            .Select(r => new { EventId = r.StatusHistoryId!.Value, r.RejectionReasonId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var reasonIds = marks.Select(m => m.RejectionReasonId).Distinct().ToList();
        var catalog = await _context.RejectionReasons
            .AsNoTracking()
            .Where(r => reasonIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Description })
            .ToDictionaryAsync(r => r.Id, r => r.Description, cancellationToken)
            .ConfigureAwait(false);

        return marks
            .GroupBy(m => m.EventId)
            .ToDictionary(
                g => g.Key,
                g => g
                    .Select(m => catalog.GetValueOrDefault(m.RejectionReasonId, "(causal retirada)"))
                    .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                    .ToList());
    }

    private static OtReportRowDto ToReportRowDto(
        ReportRow row,
        IReadOnlyDictionary<Guid, string> names,
        IReadOnlyDictionary<Guid, List<string>> causales,
        IReadOnlyDictionary<Guid, string> revisores) =>
        new(
            ProcedureInstanceId: row.Instance.Id,
            ReferenceNumber: row.Instance.ReferenceNumber,
            Placa: row.Instance.Plate,
            Vin: row.Instance.Vin,
            ClientTenantId: row.Instance.TenantId,
            ClientTenantName: names.GetValueOrDefault(row.Instance.TenantId, "(empresa desconocida)"),
            Modalidad: row.Instance.ModalidadEntrada,
            Status: row.Instance.Status,
            EstadoOt: row.EstadoOt,
            Prioritario: row.Instance.Prioritario,
            SubsanacionActiva: row.Instance.SubsanacionActiva,
            RadicadoEn: row.RadicadoEn,
            UltimaRadicacionEn: row.UltimaRadicacionEn,
            DecididoEn: row.DecididoEn,
            // Decidido pero sin usuario resoluble = decisión de sistema (webhook del organismo,
            // consulta Quipux). Decirlo es más útil que dejar la celda vacía.
            DecididoPor: row.DecididoPor is Guid userId
                ? revisores.GetValueOrDefault(userId, "(usuario desconocido)")
                : row.DecididoEn is null ? null : "(automático)",
            HorasHastaDecision: row.HorasHastaDecision,
            DiasEnOrganismo: row.DiasEnOrganismo,
            Devoluciones: row.Devoluciones,
            CausalesUltimoRechazo: row.UltimoRechazoEventId is Guid eventId
                ? causales.GetValueOrDefault(eventId, [])
                : []);

    // ── C. Informe de revisores ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Evento de historial con su id. El id hace falta para colgar las causales del rechazo, que es
    /// lo que distingue «rechacé 20» de «rechacé 20 marcando 9 causales cada vez».
    /// </summary>
    private sealed record ReviewerEventRow(
        Guid Id,
        Guid InstanceId,
        string ToStatus,
        DateTimeOffset ChangedAt,
        Guid? ChangedBy);

    public Task<OtReviewersReportDto?> GetReviewersReportAsync(
        Guid otTenantId,
        OtReviewersQuery query,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default) =>
        ExecuteScopedAsync<OtReviewersReportDto>(
            otTenantId,
            transitOfficeIdOverride,
            async (transitOfficeId, tenantIds) =>
            {
                var filter = query.Filter;
                var (from, to) = BogotaDayRange(filter.From, filter.To);

                var instances = await QueryInstances(transitOfficeId, tenantIds, filter)
                    .Select(p => new { p.Id, p.TenantId, p.Prioritario })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var instanceIds = instances.Select(i => i.Id).ToList();
                var prioritarios = instances.Where(i => i.Prioritario).Select(i => i.Id).ToHashSet();
                var instanceTenant = instances.ToDictionary(i => i.Id, i => i.TenantId);

                // Historial COMPLETO, sin recortar por rango: la reincidencia pregunta si un rechazo
                // volvió a rechazarse DESPUÉS, y ese segundo rechazo suele caer fuera del periodo.
                var history = await _context.ProcedureInstanceStatusHistories
                    .AsNoTracking()
                    .Where(h => instanceIds.Contains(h.ProcedureInstanceId))
                    .Select(h => new ReviewerEventRow(
                        h.Id, h.ProcedureInstanceId, h.ToStatus, h.ChangedAt, h.ChangedBy))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var deliveries = history
                    .Where(h => h.ToStatus == TramiteEstado.Entregado)
                    .GroupBy(h => h.InstanceId)
                    .ToDictionary(g => g.Key, g => g.Select(h => h.ChangedAt).OrderBy(d => d).ToList());

                var rejections = history
                    .Where(h => h.ToStatus == TramiteEstado.Rechazado)
                    .GroupBy(h => h.InstanceId)
                    .ToDictionary(g => g.Key, g => g.Select(h => h.ChangedAt).OrderBy(d => d).ToList());

                // El universo son las DECISIONES del rango, no los trámites recibidos: la pregunta
                // es qué hizo esta persona en estas fechas.
                var decisiones = history
                    .Where(h => IsDecision(h.ToStatus)
                        && h.ChangedBy is not null
                        && h.ChangedAt >= from && h.ChangedAt <= to)
                    .ToList();

                // Filtro vacío = todos los revisores. Devolver cero filas cuando nadie ha tocado el
                // selector dejaría el informe inservible justo al abrirlo.
                if (query.UserIds.Count > 0)
                {
                    var elegidos = query.UserIds.ToHashSet();
                    decisiones = decisiones.Where(d => elegidos.Contains(d.ChangedBy!.Value)).ToList();
                }

                var marcas = await CountRejectionMarksAsync(
                    decisiones
                        .Where(d => d.ToStatus == TramiteEstado.Rechazado)
                        .Select(d => d.Id)
                        .ToList(),
                    cancellationToken)
                    .ConfigureAwait(false);

                var filas = BuildReviewerRows(
                    decisiones, deliveries, rejections, instanceTenant, prioritarios, marcas);

                var nombres = await ResolveUserNamesAsync(
                    filas.Select(f => f.UserId).ToList(), cancellationToken).ConfigureAwait(false);

                var conNombre = filas
                    .Select(f => f with
                    {
                        DisplayName = nombres.TryGetValue(f.UserId, out var name)
                            ? name
                            : "(usuario desconocido)",
                    })
                    .ToList();

                var ordenadas = SortReviewerRows(conNombre, query.SortBy, query.Descending);

                return new OtReviewersReportDto(
                    BuildReviewersSummary(ordenadas, decisiones, deliveries),
                    ordenadas);
            },
            cancellationToken);

    private static List<OtReviewerRowDto> BuildReviewerRows(
        IReadOnlyList<ReviewerEventRow> decisiones,
        Dictionary<Guid, List<DateTimeOffset>> deliveries,
        Dictionary<Guid, List<DateTimeOffset>> rejections,
        Dictionary<Guid, Guid> instanceTenant,
        HashSet<Guid> prioritarios,
        Dictionary<Guid, int> marcasPorEvento) =>
        decisiones
            .GroupBy(d => d.ChangedBy!.Value)
            .Select(g =>
            {
                var propias = g.ToList();
                var aprobados = propias.Count(d => d.ToStatus == TramiteEstado.Aprobado);
                var rechazos = propias.Where(d => d.ToStatus == TramiteEstado.Rechazado).ToList();

                // Solo las decisiones con radicación previa tienen reloj. Las demás no se cuentan ni
                // como cero: un cero inventado bajaría la mediana de todo el equipo.
                var horas = propias
                    .Select(d => LastDeliveryBefore(deliveries, d.InstanceId, d.ChangedAt))
                    .Where(h => h is not null)
                    .Select(h => h!.Value)
                    .ToList();

                var reincidentes = rechazos.Count(d =>
                    rejections.TryGetValue(d.InstanceId, out var all)
                    && all.Any(at => at > d.ChangedAt));

                var marcas = rechazos.Sum(d => marcasPorEvento.GetValueOrDefault(d.Id));

                // Días ACTIVOS en calendario de Bogotá, no días del rango: quien estuvo de
                // vacaciones media semana no debe parecer menos productivo por ello.
                var diasActivos = propias
                    .Select(d => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(d.ChangedAt, Bogota).DateTime))
                    .Distinct()
                    .Count();

                return new OtReviewerRowDto(
                    UserId: g.Key,
                    DisplayName: string.Empty,
                    Decididos: propias.Count,
                    Aprobados: aprobados,
                    AprobacionPct: Pct(aprobados, propias.Count),
                    Rechazados: rechazos.Count,
                    RechazoPct: Pct(rechazos.Count, propias.Count),
                    TiempoMedianoHoras: Median(horas),
                    TiempoPromedioHoras: horas.Count == 0 ? null : Math.Round(horas.Average(), 2),
                    TiempoP90Horas: Percentile(horas, 0.9),
                    TiempoMaximoHoras: horas.Count == 0 ? null : Math.Round(horas.Max(), 2),
                    EnMenosDe24hPct: Pct(horas.Count(h => h < 24), horas.Count),
                    VuelvenARechazarsePct: Pct(reincidentes, rechazos.Count),
                    CausalesPorRechazo: rechazos.Count == 0
                        ? 0
                        : Math.Round(marcas / (double)rechazos.Count, 2),
                    DiasActivos: diasActivos,
                    DecisionesPorDiaActivo: diasActivos == 0
                        ? 0
                        : Math.Round(propias.Count / (double)diasActivos, 2),
                    EmpresasAtendidas: propias
                        .Select(d => instanceTenant.GetValueOrDefault(d.InstanceId))
                        .Where(t => t != Guid.Empty)
                        .Distinct()
                        .Count(),
                    PrioritariosDecididos: propias.Count(d => prioritarios.Contains(d.InstanceId)),
                    PrimeraDecision: propias.Min(d => d.ChangedAt),
                    UltimaDecision: propias.Max(d => d.ChangedAt));
            })
            .ToList();

    /// <summary>
    /// El equipo en conjunto. La mediana y el p90 se calculan sobre TODAS las decisiones, no
    /// promediando las medianas de cada persona: el promedio de medianas le da el mismo peso a quien
    /// decidió tres casos que a quien decidió trescientos.
    /// </summary>
    private static OtReviewersSummaryDto BuildReviewersSummary(
        List<OtReviewerRowDto> filas,
        List<ReviewerEventRow> decisiones,
        Dictionary<Guid, List<DateTimeOffset>> deliveries)
    {
        var horas = decisiones
            .Select(d => LastDeliveryBefore(deliveries, d.InstanceId, d.ChangedAt))
            .Where(h => h is not null)
            .Select(h => h!.Value)
            .ToList();

        var decididos = filas.Sum(f => f.Decididos);
        var aprobados = filas.Sum(f => f.Aprobados);
        var lider = filas.OrderByDescending(f => f.Decididos).FirstOrDefault();

        return new OtReviewersSummaryDto(
            Revisores: filas.Count,
            Decididos: decididos,
            Aprobados: aprobados,
            Rechazados: filas.Sum(f => f.Rechazados),
            AprobacionPct: Pct(aprobados, decididos),
            TiempoMedianoHoras: Median(horas),
            TiempoP90Horas: Percentile(horas, 0.9),
            ConcentracionTopPct: Pct(lider?.Decididos ?? 0, decididos),
            RevisorMasActivo: lider?.DisplayName);
    }

    private static List<OtReviewerRowDto> SortReviewerRows(
        List<OtReviewerRowDto> filas,
        string? sortBy,
        bool descending)
    {
        // Desempate por nombre para que el orden sea estable: sin él, dos revisores con el mismo
        // volumen pueden intercambiarse entre recargas y parecer que la tabla cambió.
        IOrderedEnumerable<OtReviewerRowDto> ordered = sortBy switch
        {
            OtReviewerSort.Nombre => descending
                ? filas.OrderByDescending(f => f.DisplayName)
                : filas.OrderBy(f => f.DisplayName),
            OtReviewerSort.Aprobacion => descending
                ? filas.OrderByDescending(f => f.AprobacionPct)
                : filas.OrderBy(f => f.AprobacionPct),
            OtReviewerSort.Rechazo => descending
                ? filas.OrderByDescending(f => f.RechazoPct)
                : filas.OrderBy(f => f.RechazoPct),
            // Sin tiempo medible va al final en ambos sentidos: «—» no es ni rápido ni lento.
            OtReviewerSort.Tiempo => descending
                ? filas.OrderByDescending(f => f.TiempoMedianoHoras ?? double.MinValue)
                : filas.OrderBy(f => f.TiempoMedianoHoras ?? double.MaxValue),
            OtReviewerSort.Reincidencia => descending
                ? filas.OrderByDescending(f => f.VuelvenARechazarsePct)
                : filas.OrderBy(f => f.VuelvenARechazarsePct),
            OtReviewerSort.Actividad => descending
                ? filas.OrderByDescending(f => f.DecisionesPorDiaActivo)
                : filas.OrderBy(f => f.DecisionesPorDiaActivo),
            _ => descending
                ? filas.OrderByDescending(f => f.Decididos)
                : filas.OrderBy(f => f.Decididos),
        };

        return ordered.ThenBy(f => f.DisplayName).ToList();
    }

    /// <summary>Cuántas causales lleva marcada cada evento de rechazo.</summary>
    private async Task<Dictionary<Guid, int>> CountRejectionMarksAsync(
        List<Guid> eventIds,
        CancellationToken cancellationToken)
    {
        if (eventIds.Count == 0)
        {
            return [];
        }

        var marks = await _context.ProcedureInstanceRejectionReasons
            .AsNoTracking()
            .Where(r => r.StatusHistoryId != null && eventIds.Contains(r.StatusHistoryId!.Value))
            .GroupBy(r => r.StatusHistoryId!.Value)
            .Select(g => new { EventId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return marks.ToDictionary(m => m.EventId, m => m.Count);
    }

    public Task<IReadOnlyList<OtReviewerOptionDto>?> ListReviewerOptionsAsync(
        Guid otTenantId,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default) =>
        ExecuteScopedAsync<IReadOnlyList<OtReviewerOptionDto>>(
            otTenantId,
            transitOfficeIdOverride,
            async (transitOfficeId, tenantIds) =>
            {
                // Filtro vacío a propósito: el catálogo de revisores no depende de modalidad ni de
                // empresa, igual que el de empresas no depende del rango.
                var instanceIds = await QueryInstances(
                        transitOfficeId, tenantIds, new OtMetricsFilter(default, default))
                    .Select(p => p.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var counts = await _context.ProcedureInstanceStatusHistories
                    .AsNoTracking()
                    .Where(h => instanceIds.Contains(h.ProcedureInstanceId)
                        && h.ChangedBy != null
                        && (h.ToStatus == TramiteEstado.Aprobado || h.ToStatus == TramiteEstado.Rechazado))
                    .GroupBy(h => h.ChangedBy!.Value)
                    .Select(g => new { UserId = g.Key, Decisiones = g.Count() })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var nombres = await ResolveUserNamesAsync(
                    counts.Select(c => c.UserId).ToList(), cancellationToken).ConfigureAwait(false);

                return counts
                    .Select(c => new OtReviewerOptionDto(
                        c.UserId,
                        nombres.TryGetValue(c.UserId, out var name) ? name : "(usuario desconocido)",
                        c.Decisiones))
                    .OrderByDescending(o => o.Decisiones)
                    .ThenBy(o => o.DisplayName)
                    .ToList();
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

    // ── Alertas por umbral (Reportes 2.0, HU-D — alcance OT) ──────────────────────────────────

    public Task<OtAlertSnapshotDto?> GetAlertSnapshotAsync(
        Guid otTenantId,
        int windowMinutes,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default) =>
        ExecuteScopedAsync<OtAlertSnapshotDto>(
            otTenantId,
            transitOfficeIdOverride,
            async (transitOfficeId, tenantIds) =>
            {
                // Sin filtro de modalidad/empresa cliente a propósito: una alerta vigila TODO el
                // organismo, no un recorte — mismo criterio que las métricas de alerta de empresa
                // (AlertMetricsReadRepository), que tampoco aceptan filtros adicionales.
                var wildcard = new OtMetricsFilter(default, default);

                var pendientes = await LoadPendingAsync(
                    transitOfficeId, tenantIds, wildcard, cancellationToken).ConfigureAwait(false);
                var stuckCount = pendientes.Count(IsMasDe7Dias);

                var since = DateTimeOffset.UtcNow.AddMinutes(-windowMinutes);
                var decisiones = await QueryTransitions(
                        transitOfficeId, tenantIds, wildcard, since, DateTimeOffset.UtcNow)
                    .Where(h => h.ToStatus == TramiteEstado.Aprobado || h.ToStatus == TramiteEstado.Rechazado)
                    .Select(h => h.ToStatus)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var rechazados = decisiones.Count(s => s == TramiteEstado.Rechazado);
                // 0 sin decididos: mismo default que rejection_rate_pct de empresa — una ventana
                // sin actividad no es una tasa de rechazo del 0 %, es "no hay nada que medir".
                var rejectionRatePct = decisiones.Count == 0
                    ? 0m
                    : Math.Round(rechazados * 100m / decisiones.Count, 2);

                return new OtAlertSnapshotDto(stuckCount, rejectionRatePct);
            },
            cancellationToken);

    public Task<Guid?> ResolveTenantIdForTransitOfficeAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken = default) =>
        _scope.ReadCrossTenantAsync(
            () => _context.TransitOfficeProfiles
                .AsNoTracking()
                .Where(p => p.TransitOfficeId == transitOfficeId)
                .Select(p => (Guid?)p.TenantId)
                .FirstOrDefaultAsync(cancellationToken),
            cancellationToken);

    // ── Scope OT (grant + organismo) ──────────────────────────────────────────────────────────

    // La resolución del organismo y sus empresas vive en OtTenantScope: es la única regla que
    // separa los trámites de un organismo de los de otro, y desde que hay un segundo repositorio con
    // este mismo eje invertido tenerla escrita dos veces sería una fuga esperando a que las copias
    // se desincronicen.
    private Task<T?> ExecuteScopedAsync<T>(
        Guid otTenantId,
        Guid? transitOfficeIdOverride,
        Func<Guid, IReadOnlyList<Guid>, Task<T>> action,
        CancellationToken cancellationToken)
        where T : class =>
        _scope.ExecuteAsync(otTenantId, transitOfficeIdOverride, action, cancellationToken);

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
        HistoryRow decision) =>
        LastDeliveryBefore(deliveries, decision.InstanceId, decision.ChangedAt);

    /// <summary>
    /// Horas desde la última radicación anterior a la decisión. Es el turno que el organismo
    /// trabajó: medir desde la PRIMERA radicación le cargaría además el tiempo que la empresa
    /// tardó en subsanar.
    /// </summary>
    private static double? LastDeliveryBefore(
        Dictionary<Guid, List<DateTimeOffset>> deliveries,
        Guid instanceId,
        DateTimeOffset decidedAt)
    {
        if (!deliveries.TryGetValue(instanceId, out var entregas))
        {
            return null;
        }

        DateTimeOffset? last = null;
        foreach (var at in entregas)
        {
            if (at <= decidedAt)
            {
                last = at;
            }
        }

        return last is null ? null : (decidedAt - last.Value).TotalHours;
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

    /// <summary>
    /// Percentil por interpolación lineal. Acompaña siempre a la mediana en el informe: p50 y p90
    /// juntos distinguen «casi todo tarda esto» de «la mitad tarda esto y la otra mitad quién sabe».
    /// </summary>
    private static double? Percentile(List<double> values, double q)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var ordered = values.OrderBy(v => v).ToList();
        if (ordered.Count == 1)
        {
            return Math.Round(ordered[0], 2);
        }

        var position = q * (ordered.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);

        return Math.Round(
            ordered[lower] + ((ordered[upper] - ordered[lower]) * (position - lower)),
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
    private static (DateTimeOffset From, DateTimeOffset To) BogotaDayRange(DateOnly from, DateOnly to) =>
        OtTenantScope.DayRange(from, to);
}
