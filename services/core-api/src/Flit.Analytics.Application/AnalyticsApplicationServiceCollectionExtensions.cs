using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Queries;
using Flit.Analytics.Application.Queries.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Analytics.Application;

/// <summary>Registro DI de los handlers de lectura del dashboard analítico (Feature #10139).</summary>
public static class AnalyticsApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddAnalyticsApplication(this IServiceCollection services)
    {
        services.AddScoped<GetAnalyticsOverviewHandler>();
        services.AddScoped<GetTopProducersHandler>();
        services.AddScoped<GetProcedureDetailsHandler>();
        services.AddScoped<ExportExecutivePdfHandler>();
        services.AddScoped<GetMonthlyTrendHandler>();
        services.AddScoped<GetDetailedProceduresHandler>(); // Feature #10813 HU #10815

        // Reportes2 HU-B — handlers de métricas (§4.2–§4.5 del contrato).
        services.AddScoped<GetOtMetricsHandler>();
        services.AddScoped<GetFunnelHandler>();
        services.AddScoped<GetUsageHandler>();
        services.AddScoped<GetLiveOverviewHandler>();
        // Reportes2 HU-B — fallback sin datos de la telemetría HU-A: TryAdd cede ante la
        // implementación real (Flit.Infrastructure) cuando se integre; sin ella el host arranca
        // y los endpoints devuelven listas vacías (§4.3/§4.4).
        services.TryAddScoped<IUsageMetricsReadRepository, EmptyUsageMetricsReadRepository>();
        return services;
    }
}
