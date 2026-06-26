using Flit.Analytics.Application.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Analytics.Application;

/// <summary>Registro DI de los handlers de lectura del dashboard analítico (Feature #10139).</summary>
public static class AnalyticsApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddAnalyticsApplication(this IServiceCollection services)
    {
        services.AddScoped<GetAnalyticsOverviewHandler>();
        services.AddScoped<GetTopProducersHandler>();
        services.AddScoped<GetProcedureDetailsHandler>();
        return services;
    }
}
