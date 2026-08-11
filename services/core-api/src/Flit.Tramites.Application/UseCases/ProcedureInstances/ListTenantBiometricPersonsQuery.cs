using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;
using Flit.Tramites.Domain.ReadModels;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Fila de la grilla agrupada por persona (HU #11270 / CF-05): estado de la más reciente,
/// contador y peor alerta del grupo.
/// </summary>
public sealed record TenantBiometricPersonDto(
    string DocumentType,
    string DocumentNumber,
    string Name,
    string Status,
    int ValidationCount,
    string? WorstAlertKind,
    Guid LatestValidationId,
    Guid? InstanceId,
    string? ReferenceNumber,
    string? Modalidad,
    string? PartyRole,
    string Email,
    string Provider,
    int? Score,
    string? CaptureUrl,
    bool Expired,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ValidatedAt,
    DateTimeOffset? ValidUntil,
    int? DaysRemaining,
    DateTimeOffset? LinkExpiresAt);

/// <summary>
/// Respuesta del listado agrupado. <see cref="Stats"/> cuenta PERSONAS por el estado de su validación
/// más reciente — la grilla muestra una fila por documento, así que los KPIs deben cuadrar con ella
/// (antes contaban validaciones, D7, y no coincidían con las filas). <see cref="Total"/> es el total de
/// personas del conjunto filtrado (paginador) y equivale a <c>Stats.Total</c>.
/// </summary>
public sealed record TenantBiometricPersonsResponse(
    IReadOnlyList<TenantBiometricPersonDto> Persons,
    BiometricValidationStatsDto Stats,
    int Page,
    int PageSize,
    int Total);

/// <summary>Query string de GET /biometric-validations/by-person (solo filtros de persona).</summary>
public sealed record TenantBiometricPersonListQuery(
    string? Name = null,
    string? DocumentType = null,
    string? DocumentNumber = null,
    string? Status = null,
    DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedTo = null,
    string? VigenciaEstado = null,
    DateTimeOffset? ExpiraDesde = null,
    DateTimeOffset? ExpiraHasta = null,
    int? VenceEnDias = null,
    int Page = 1,
    int PageSize = 20,
    bool? Standalone = null)
{
    public int SafePage() => Page < 1 ? 1 : Page;
    public int SafePageSize() => Math.Clamp(
        PageSize,
        TenantBiometricValidationListQuery.MinPageSize,
        TenantBiometricValidationListQuery.MaxPageSize);

    public string? Validate()
    {
        // Reusa el validador del listado plano para estado/vigencia/fechas (mismos catálogos).
        var proxy = new TenantBiometricValidationListQuery(
            Status: Status,
            CreatedFrom: CreatedFrom,
            CreatedTo: CreatedTo,
            VigenciaEstado: VigenciaEstado,
            ExpiraDesde: ExpiraDesde,
            ExpiraHasta: ExpiraHasta,
            VenceEnDias: VenceEnDias,
            Page: Page,
            PageSize: PageSize,
            Standalone: Standalone);
        return proxy.Validate();
    }

    public BiometricPersonGroupFilter ToFilter() => new()
    {
        Name = TrimCap(Name),
        DocumentType = TrimCap(DocumentType),
        DocumentNumber = TrimCap(DocumentNumber),
        Status = string.IsNullOrWhiteSpace(Status) ? null : Status.Trim(),
        CreatedFrom = CreatedFrom,
        CreatedTo = CreatedTo,
        VigenciaEstado = string.IsNullOrWhiteSpace(VigenciaEstado)
            ? null
            : VigenciaEstado.Trim().ToLowerInvariant(),
        ExpiraDesde = ExpiraDesde,
        ExpiraHasta = ExpiraHasta,
        VenceEnDias = VenceEnDias,
        Standalone = Standalone,
    };

    private static string? TrimCap(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length > TenantBiometricValidationListQuery.TextFilterMaxLength
            ? trimmed[..TenantBiometricValidationListQuery.TextFilterMaxLength]
            : trimmed;
    }
}

/// <summary>
/// Lista personas (documento normalizado) del tenant con su validación más reciente, contador y
/// peor alerta. Endpoint propio — no altera GET /biometric-validations (Dashboard/Prevalidaciones).
/// </summary>
public sealed class ListTenantBiometricPersonsHandler(
    IProcedureInstanceRepository repo,
    IIdentityValidationOutboxRepository outboxRepo)
{
    /// <summary>Ventana CF-05 para alertas sobre validaciones terminales.</summary>
    public const int AlertWindowDays = 30;

    private static readonly string[] AlertSeverityOrder =
    [
        IdentityValidationAlertKinds.Atascada,
        IdentityValidationAlertKinds.Rechazada,
        IdentityValidationAlertKinds.Expirada,
        IdentityValidationAlertKinds.PorVencer,
    ];

    public async Task<(TenantBiometricPersonsResponse? Result, string? Error)> HandleAsync(
        Guid tenantId,
        TenantBiometricPersonListQuery? query = null,
        CancellationToken ct = default)
    {
        query ??= new TenantBiometricPersonListQuery();
        var validationError = query.Validate();
        if (validationError is not null)
            return (null, validationError);

        var filter = query.ToFilter();
        var activeFilter = filter.HasActiveFilters ? filter : null;
        var page = query.SafePage();
        var pageSize = query.SafePageSize();
        var now = DateTimeOffset.UtcNow;

        // KPIs = PERSONAS, no validaciones: esta grilla pinta una fila por documento, así que los
        // contadores tienen que cuadrar con lo que el gestor ve. Cada persona aporta el estado de su
        // validación más reciente, sobre el mismo conjunto filtrado que la página.
        var stats = ListTenantBiometricValidationsHandler.BuildStats(
            await repo.CountBiometricPersonsByEstadoAsync(tenantId, activeFilter, now, ct));

        var (rows, totalPersons) = await repo.ListBiometricValidationsGroupedByPersonAsync(
            tenantId, (page - 1) * pageSize, pageSize, activeFilter, now, ct);

        var stuckIds = await StuckIdsAsync(tenantId, ct);
        var worstByPerson = await ResolveWorstAlertsAsync(tenantId, rows, stuckIds, now, ct);

        var persons = rows.Select(r =>
        {
            var key = $"{r.DocumentTypeNorm}|{r.DocumentNumberNorm}";
            worstByPerson.TryGetValue(key, out var worst);
            return ToDto(r, worst, now);
        }).ToList();

        return (new TenantBiometricPersonsResponse(persons, stats, page, pageSize, totalPersons), null);
    }

    private async Task<IReadOnlyDictionary<string, string?>> ResolveWorstAlertsAsync(
        Guid tenantId,
        IReadOnlyList<BiometricPersonGroupProjection> rows,
        IReadOnlySet<Guid> stuckIds,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (rows.Count == 0)
            return new Dictionary<string, string?>();

        var docs = rows
            .Select(r => (r.DocumentTypeNorm, r.DocumentNumberNorm))
            .Distinct()
            .ToList();

        var scan = await repo.ListBiometricValidationsForPersonAlertScanAsync(
            tenantId, docs, AlertWindowDays, now, ct);

        var worst = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var group in scan.GroupBy(v =>
                     $"{DocumentCanonicalNormalization.NormalizePart(v.DocumentType)}|{DocumentCanonicalNormalization.NormalizePart(v.DocumentNumber)}"))
        {
            string? best = null;
            var bestRank = int.MaxValue;
            foreach (var v in group)
            {
                var kind = ClassifyAlert(v, stuckIds, now);
                if (kind is null)
                    continue;
                var rank = Array.IndexOf(AlertSeverityOrder, kind);
                if (rank < 0 || rank >= bestRank)
                    continue;
                bestRank = rank;
                best = kind;
            }

            worst[group.Key] = best;
        }

        return worst;
    }

    /// <summary>
    /// Misma prioridad que <see cref="ListIdentityValidationAlertsHandler"/> Classify, expuesta para
    /// la peor alerta por persona (atascada &gt; rechazada &gt; expirada &gt; por_vencer).
    /// </summary>
    internal static string? ClassifyAlert(
        ProcedureInstanceBiometricValidation v,
        IReadOnlySet<Guid> stuckIds,
        DateTimeOffset now)
    {
        if (stuckIds.Contains(v.Id))
            return IdentityValidationAlertKinds.Atascada;
        if (v.Status == BiometricEstados.Rechazado)
            return IdentityValidationAlertKinds.Rechazada;
        if (v.Status == BiometricEstados.Aprobado)
        {
            if (BiometricRules.EsAprobadaVigente(v, now))
            {
                var dias = BiometricRules.DiasRestantesVigencia(v, now);
                return dias is not null && dias <= BiometricRules.VigenciaPorVencerDias
                    ? IdentityValidationAlertKinds.PorVencer
                    : null;
            }

            return IdentityValidationAlertKinds.Expirada;
        }

        if (v.Status == BiometricEstados.Expirado || now > v.ExpiresAt)
            return IdentityValidationAlertKinds.Expirada;

        return null;
    }

    private async Task<IReadOnlySet<Guid>> StuckIdsAsync(Guid tenantId, CancellationToken ct)
    {
        var stuck = await outboxRepo.ListStuckAsync(tenantId, ListStuckIdentityValidationsHandler.MaxRows, ct);
        return stuck.Select(s => s.ValidationId).ToHashSet();
    }

    private static TenantBiometricPersonDto ToDto(
        BiometricPersonGroupProjection r,
        string? worstAlert,
        DateTimeOffset now)
    {
        // DaysRemaining solo aplica a aprobadas; reusa BiometricRules vía proyección mínima.
        int? daysRemaining = null;
        if (r.Status == BiometricEstados.Aprobado && r.ValidatedAt is { } validatedAt)
        {
            var validUntil = r.ValidUntil ?? validatedAt.AddDays(BiometricRules.VigenciaDias);
            var days = (int)Math.Ceiling((validUntil - now).TotalDays);
            daysRemaining = Math.Max(0, days);
        }

        return new TenantBiometricPersonDto(
            r.DocumentType,
            r.DocumentNumber,
            r.Name,
            r.Status,
            r.ValidationCount,
            worstAlert,
            r.LatestValidationId,
            r.ProcedureInstanceId,
            r.ReferenceNumber,
            r.Modalidad,
            r.PartyRole,
            r.Email,
            r.Provider,
            r.Score,
            r.CaptureUrl,
            r.Status != BiometricEstados.Aprobado && now > r.ExpiresAt,
            r.CreatedAt,
            r.ValidatedAt,
            r.ValidUntil,
            daysRemaining,
            r.ExpiresAt);
    }

    /// <summary>
    /// Traduce filtros de persona a filtro plano solo para KPIs (mismo subconjunto documental).
    /// Sin filtros de validación — coherente con el modo agrupado.
    /// </summary>
    private static BiometricValidationListFilter? ToFlatFilter(BiometricPersonGroupFilter? filter)
    {
        if (filter is null || !filter.HasActiveFilters)
            return null;

        return new BiometricValidationListFilter
        {
            Name = filter.Name,
            DocumentType = filter.DocumentType,
            DocumentNumber = filter.DocumentNumber,
            Status = filter.Status,
            CreatedFrom = filter.CreatedFrom,
            CreatedTo = filter.CreatedTo,
            VigenciaEstado = filter.VigenciaEstado,
            ExpiraDesde = filter.ExpiraDesde,
            ExpiraHasta = filter.ExpiraHasta,
            VenceEnDias = filter.VenceEnDias,
            Standalone = filter.Standalone,
        };
    }
}
