using Flit.Admin.Domain.Auditing;
using Flit.Admin.Domain.Companies;
using Flit.Analytics.Application.CompanyQueries;
using Flit.Analytics.Application.Dtos;
using Flit.Analytics.Application.Queries.Metrics;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Queries.Domain;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Integration.Tests.Tenancy;

/// <summary>
/// Cómo se ejecuta una consulta del inventario para un lector dado. <c>reader == null</c> significa
/// SuperAdmin / modo global (solo las consultas con <see cref="CoveredQuery.SupportsGlobal"/>).
/// </summary>
/// <param name="RowIds">Ids de las filas devueltas (todos los ids sembrados codifican su tenant dueño).</param>
/// <param name="Scalar">Valor agregado para consultas sin filas identificables (conteos, métricas).</param>
internal sealed record QueryOutcome(IReadOnlyList<Guid>? RowIds, long? Scalar);

/// <summary>
/// Una consulta alcanzada por el Feature #12254 (recibe identificador de cliente o alcance).
/// </summary>
/// <param name="Id">Código estable (<c>Q01</c>…), citado en el README y en las evidencias.</param>
/// <param name="Query">Repositorio.Método tal como existe en producto.</param>
/// <param name="ScopeParameter">Parámetro por el que entra el cliente.</param>
/// <param name="OwnRows">Filas (o valor agregado) que devuelve para un cliente con la semilla simétrica.</param>
/// <param name="SupportsGlobal"><c>true</c> si acepta <c>null</c> = todos los tenants (SuperAdmin).</param>
/// <param name="TenantOnly"><c>true</c> si NO acepta lector por tenant (solo modo global).</param>
internal sealed record CoveredQuery(
    string Id,
    string Query,
    string ScopeParameter,
    int OwnRows,
    bool SupportsGlobal,
    bool TenantOnly = false)
{
    public override string ToString() => $"{Id} {Query}";
}

/// <summary>
/// HU #12322 (AC6) — inventario de consultas de lectura que reciben identificador de cliente y su
/// runner contra los repositorios REALES sobre la base efímera. Es la fuente única de las suites
/// <c>TenantLeakTests</c> y <c>TenantParityTests</c> (ambas iteran <see cref="All"/>) y de la tabla
/// «Cobertura #12322» del README: una consulta que no esté aquí no está cubierta.
/// <para>
/// Consultas que NO se cubren aquí y por qué (ver README): <c>ImprontaRepository.ListAsync</c>
/// (vista global cross-tenant por diseño, ADR-0022, sin parámetro tenant); listado de usuarios de
/// <c>GET /security/users</c> (LINQ inline en <c>SecurityEndpoints</c>, sin repositorio: exige
/// <c>WebApplicationFactory</c>); <c>OtMetricsReadRepository.Get*</c> distintos de
/// <c>ListClientCompaniesAsync</c> (mismo <c>OtTenantScope</c> por grants, ya ejercitado en Q24/Q25);
/// <c>MandateConfigAdminService</c> (servicio de configuración con dependencias de documento, no
/// consulta de listado).
/// </para>
/// </summary>
internal static class CoveredQueries
{
    private static readonly DateOnly From = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10));
    private static readonly DateOnly To = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

    public static readonly IReadOnlyList<CoveredQuery> All =
    [
        new("Q01", "ProcedureInstanceRepository.ListWithSummaryGraphAsync", "Guid? tenantId", 2, SupportsGlobal: true),
        new("Q02", "ProcedureInstanceRepository.ListWithSummaryGraphFilteredAsync", "Guid? tenantId", 2, SupportsGlobal: true),
        new("Q03", "ProcedureInstanceRepository.CountByStatusFilteredAsync", "Guid? tenantId", 2, SupportsGlobal: true),
        new("Q04", "ProcedureInstanceRepository.GetFilterOptionsAsync (Companias)", "Guid? tenantId", 0, SupportsGlobal: true),
        new("Q05", "ProcedureInstanceRepository.GetByIdWithDetailsAsync (detalle)", "Guid tenantId", 2, SupportsGlobal: false),
        new("Q06", "ProcedureInstanceRepository.CountByTenantAndYearAsync", "Guid tenantId", 2, SupportsGlobal: false),
        new("Q07", "ProcedureInstanceRepository.GetStatusHistoryPageAsync (historial de estados)", "Guid tenantId", 2, SupportsGlobal: false),
        new("Q08", "ListProcedureInstancesFilteredHandler (historial por placa, request de PlateHistoryScope)", "Guid? TenantId", 1, SupportsGlobal: true),
        new("Q09", "CompanyReadRepository.ListAsync (listado de compañías)", "— (SuperAdmin)", 1, SupportsGlobal: true, TenantOnly: true),
        new("Q10", "CompanyReadRepository.GetByIdAsync", "Guid tenantId", 1, SupportsGlobal: false),
        new("Q11", "NotificationDeliveryLogRepository.ListByTenantAsync", "Guid tenantId", 1, SupportsGlobal: false),
        new("Q12", "CompanyDocumentParamRepository.ListByTenantAsync", "Guid tenantId", 1, SupportsGlobal: false),
        new("Q13", "CompanyAgreementRepository.ListActiveOfficeIdsAsync", "Guid companyTenantId", 1, SupportsGlobal: false),
        new("Q14", "AdminAuditLogRepository.ListPagedAsync", "Guid? filter.TenantId", 1, SupportsGlobal: true),
        new("Q15", "InvitationRepository.ListPendingByTenantAsync", "Guid tenantId", 1, SupportsGlobal: false),
        new("Q16", "AnalyticsReadRepository.GetOverviewAsync", "Guid? tenantId", 2, SupportsGlobal: true),
        new("Q17", "AnalyticsReadRepository.GetTopProducersAsync", "Guid? tenantId", 1, SupportsGlobal: true),
        new("Q18", "AnalyticsReadRepository.GetMonthlyTrendAsync", "Guid? tenantId", 2, SupportsGlobal: true),
        new("Q19", "AnalyticsReadRepository.GetProcedureDetailsAsync", "Guid tenantId", 2, SupportsGlobal: false),
        new("Q20", "AnalyticsMetricsReadRepository.GetLiveOverviewAsync", "Guid tenantId", 2, SupportsGlobal: false),
        new("Q21", "AnalyticsMetricsReadRepository.GetFunnelAsync", "MetricsFilter.TenantId", 2, SupportsGlobal: false),
        new("Q22", "DetailedReportReadRepository.GetProceduresAsync", "DetailedReportFilter.TenantId", 2, SupportsGlobal: false),
        new("Q23", "CompanyQueryRepository.ExecuteAsync / ExecuteForSuperAdminAsync", "Guid tenantId / todos", 2, SupportsGlobal: true),
    ];

    /// <summary>
    /// Consultas con lector distinto (organismo de tránsito, resolver, extensión de alcance) que se
    /// cubren con pruebas dedicadas en vez del runner genérico.
    /// </summary>
    public static readonly IReadOnlyList<CoveredQuery> Dedicated =
    [
        new("Q24", "OtClientProcedureRepository.ListAsync (bandeja OT por grants)", "Guid otTenantId", 1, SupportsGlobal: false),
        new("Q25", "OtMetricsReadRepository.ListClientCompaniesAsync (grants)", "Guid otTenantId", 1, SupportsGlobal: false),
        new("Q26", "DbTenantScopeResolver.ResolveAsync", "Guid tenantId", 0, SupportsGlobal: false),
        new("Q27", "TenantScopeQueryableExtensions.WhereTenantInScope (ruta nueva)", "TenantScope", 2, SupportsGlobal: true),
    ];

    public static CoveredQuery Get(string id) =>
        All.Concat(Dedicated).Single(q => q.Id == id);

    /// <summary>Ejecuta la consulta <paramref name="id"/> como <paramref name="reader"/> (<c>null</c> = SuperAdmin).</summary>
    public static async Task<QueryOutcome> RunAsync(string id, FlitDbContext ctx, Guid? reader, CancellationToken ct = default)
    {
        var query = Get(id);
        if (reader is null && !query.SupportsGlobal)
            throw new NotSupportedException($"{id} no tiene modo global.");
        if (reader is not null && query.TenantOnly)
            throw new NotSupportedException($"{id} no recibe tenant.");

        var tenant = reader ?? Guid.Empty;
        var year = DateTime.UtcNow.Year;

        switch (id)
        {
            case "Q01":
                {
                    var rows = await new ProcedureInstanceRepository(ctx).ListWithSummaryGraphAsync(reader, 100, ct);
                    return Rows(rows.Select(r => r.Id));
                }

            case "Q02":
                {
                    var (items, _) = await new ProcedureInstanceRepository(ctx).ListWithSummaryGraphFilteredAsync(
                        reader, 0, 100, new ProcedureInstanceListFilter(), ProcedureInstanceSortBy.Default, SortDirection.Descending, ct);
                    return Rows(items.Select(r => r.Id));
                }

            case "Q03":
                {
                    var counts = await new ProcedureInstanceRepository(ctx).CountByStatusFilteredAsync(reader, new ProcedureInstanceListFilter(), ct);
                    return Scalar(counts.Values.Sum());
                }

            case "Q04":
                {
                    var options = await new ProcedureInstanceRepository(ctx).GetFilterOptionsAsync(reader, ct);
                    return Rows(options.Companias.Select(c => c.Id));
                }

            case "Q05":
                {
                    var repo = new ProcedureInstanceRepository(ctx);
                    var found = new List<Guid>();
                    foreach (var candidate in HierarchyScenario.Clients.SelectMany(HierarchyScenario.ProceduresOf))
                    {
                        var instance = await repo.GetByIdWithDetailsAsync(candidate, tenant, ct);
                        if (instance is not null)
                            found.Add(instance.Id);
                    }

                    return Rows(found);
                }

            case "Q06":
                return Scalar(await new ProcedureInstanceRepository(ctx).CountByTenantAndYearAsync(tenant, year, ct));

            case "Q07":
                {
                    // Se pide el historial de CADA trámite entregado del escenario como `tenant`: el de otro
                    // cliente debe responder null (no existe para ese tenant), nunca sus entradas.
                    var repo = new ProcedureInstanceRepository(ctx);
                    var entries = new List<Guid>();
                    foreach (var candidate in HierarchyScenario.Clients.Select(HierarchyScenario.DeliveredProcedureOf))
                    {
                        var page = await repo.GetStatusHistoryPageAsync(candidate, tenant, 0, 100, ct);
                        if (page is { } p)
                            entries.AddRange(p.Items.Select(e => e.Id));
                    }

                    return Rows(entries);
                }

            case "Q08":
                {
                    // Mismo request que construye PlateHistoryScope.BuildRequest (Flit.Api): tenant del
                    // alcance (null solo para SuperAdmin), placa normalizada, orden createdAt DESC.
                    var handler = new ListProcedureInstancesFilteredHandler(new ProcedureInstanceRepository(ctx));
                    var (items, _) = await handler.HandleAsync(new ProcedureInstanceListRequest
                    {
                        TenantId = reader,
                        Placa = HierarchyScenario.SharedPlate,
                        Skip = 0,
                        Take = 100,
                        SortBy = "createdAt",
                        SortDescending = true,
                    }, ct);
                    return Rows(items.Select(i => i.Id));
                }

            case "Q09":
                {
                    var page = await new CompanyReadRepository(ctx).ListAsync(new CompanyListFilter { Page = 1, PageSize = 100, ExcludeTransitOffices = true }, ct);
                    return Rows(page.Items.Select(i => i.Id));
                }

            case "Q10":
                {
                    var item = await new CompanyReadRepository(ctx).GetByIdAsync(tenant, ct);
                    return Rows(item is null ? [] : [item.Id]);
                }

            case "Q11":
                {
                    var rows = await new NotificationDeliveryLogRepository(ctx).ListByTenantAsync(tenant, 0, 100, ct);
                    return Rows(rows.Select(r => r.Id));
                }

            case "Q12":
                {
                    var rows = await new CompanyDocumentParamRepository(ctx).ListByTenantAsync(tenant, ct);
                    return Rows(rows.Select(r => r.Id));
                }

            case "Q13":
                {
                    var offices = await new CompanyAgreementRepository(ctx).ListActiveOfficeIdsAsync(tenant, ct);
                    return Scalar(offices.Count);
                }

            case "Q14":
                {
                    var page = await new AdminAuditLogRepository(ctx).ListPagedAsync(
                        new AdminAuditLogFilter { TenantId = reader, Page = 1, PageSize = 100 }, ct);
                    return Rows(page.Items.Select(i => i.Id));
                }

            case "Q15":
                {
                    var rows = await new InvitationRepository(ctx).ListPendingByTenantAsync(tenant, ct);
                    return Rows(rows.Select(r => r.InvitationId));
                }

            case "Q16":
                {
                    var categories = await new AnalyticsReadRepository(ctx).GetOverviewAsync(reader, From, To, ct);
                    return Scalar(categories.Sum(c => c.Total));
                }

            case "Q17":
                {
                    var producers = await new AnalyticsReadRepository(ctx).GetTopProducersAsync(reader, From, To, 100, ct);
                    return Rows(producers.Select(p => p.UserId));
                }

            case "Q18":
                {
                    var points = await new AnalyticsReadRepository(ctx).GetMonthlyTrendAsync(reader, From, To, ct);
                    return Scalar(points.Sum(p => p.Total));
                }

            case "Q19":
                {
                    var page = await new AnalyticsReadRepository(ctx).GetProcedureDetailsAsync(tenant, From, To, null, null, 1, 100, ct);
                    return Rows(page.Items.Select(i => i.Id));
                }

            case "Q20":
                {
                    var live = await new AnalyticsMetricsReadRepository(ctx).GetLiveOverviewAsync(tenant, 7, ct);
                    return Scalar(live.Today.ByStatus.Sum(s => s.Count));
                }

            case "Q21":
                {
                    var funnel = await new AnalyticsMetricsReadRepository(ctx).GetFunnelAsync(new MetricsFilter(tenant, From, To), ct);
                    return Scalar(funnel.States[0].Count);
                }

            case "Q22":
                {
                    var page = await new DetailedReportReadRepository(ctx).GetProceduresAsync(
                        new DetailedReportFilter(tenant, From, To, null, null, null, null, null, null, null, null, null), 1, 100, ct);
                    return Rows(page.Items.Select(i => i.Id));
                }

            case "Q23":
                {
                    var request = new QueryRequest(
                        new QueryDefinition(
                            new QueryDateFilter(CompanyQueryDateField.Creacion, QueryRangePreset.Personalizado, From, To),
                            [],
                            [CompanyQueryFieldCatalog.Radicado, CompanyQueryFieldCatalog.Placa]),
                        Page: 1,
                        PageSize: 100);
                    var repo = new CompanyQueryRepository(ctx);
                    var result = reader is null
                        ? await repo.ExecuteForSuperAdminAsync(request, ct)
                        : await repo.ExecuteAsync(tenant, request, ct);
                    return Rows(result.Filas.Select(f => f.ProcedureInstanceId));
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(id), id, "Consulta fuera del inventario genérico.");
        }
    }

    private static QueryOutcome Rows(IEnumerable<Guid> ids) => new(ids.ToList(), null);

    private static QueryOutcome Scalar(long value) => new(null, value);
}
