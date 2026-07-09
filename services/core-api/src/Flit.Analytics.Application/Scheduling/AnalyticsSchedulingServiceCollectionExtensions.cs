using Microsoft.Extensions.DependencyInjection;

namespace Flit.Analytics.Application.Scheduling;

/// <summary>
/// Registro DI de los handlers CRUD de programación de informes y alertas (Reportes 2.0, HU-D).
/// Archivo propio de HU-D: NO agrega nada a <c>AnalyticsApplicationServiceCollectionExtensions</c>
/// (propiedad de HU-B según §2 del contrato).
/// </summary>
public static class AnalyticsSchedulingServiceCollectionExtensions
{
    public static IServiceCollection AddAnalyticsScheduling(this IServiceCollection services)
    {
        services.AddScoped<ListReportSchedulesHandler>();
        services.AddScoped<CreateReportScheduleHandler>();
        services.AddScoped<UpdateReportScheduleHandler>();
        services.AddScoped<DeleteReportScheduleHandler>();
        services.AddScoped<ListAlertRulesHandler>();
        services.AddScoped<CreateAlertRuleHandler>();
        services.AddScoped<UpdateAlertRuleHandler>();
        services.AddScoped<DeleteAlertRuleHandler>();
        services.AddScoped<ListAlertEventsHandler>();
        return services;
    }
}
