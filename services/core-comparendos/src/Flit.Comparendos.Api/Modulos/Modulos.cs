using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Comparendos.Api.Modulos;

/// <summary>Registro y mapeo de los módulos del producto. Cada módulo agrega aquí sus servicios y sus endpoints.</summary>
public static class Modulos
{
    public static IServiceCollection AddModulosComparendos(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddScoped<Comun.Auditoria>();
        services.AddScoped<Catalogos.MapeadorOt>();
        services.AddScoped<Comparendos.Timeline>();

        // Ingesta (HU #13460, #13461). Las fuentes reales se conectan al final (Feature #13464); mientras tanto, simulada.
        services.AddSingleton<Ingesta.Fuentes.IFuenteComparendos, Ingesta.Fuentes.FuenteSimulada>();
        services.AddScoped<Ingesta.Reconciliador>();
        services.AddScoped<Ingesta.CicloDeIngesta>();
        services.AddScoped<Trabajos.IEjecutorTrabajo>(sp => sp.GetRequiredService<Ingesta.CicloDeIngesta>());
        services.TryAddScoped<Ingesta.IAlertasIntegracion, Ingesta.SinAlertas>();

        services.AddSingleton<Trabajos.ColaTrabajos>();
        if (configuration.GetValue("Comparendos:Trabajos:Habilitados", true))
        {
            services.AddHostedService<Trabajos.ProgramadorTrabajos>();
            services.AddHostedService<Trabajos.ProcesadorTrabajos>();
        }
        return services;
    }

    public static WebApplication MapModulosComparendos(this WebApplication app, string prefijo)
    {
        var api = app.MapGroup(prefijo);
        api.MapGet("/sesion", Sesion.Obtener).RequireAuthorization(Plataforma.AutorizacionExtensions.PoliticaUsuario);
        Monitoreo.MonitoreoEndpoints.Map(api);
        Catalogos.CatalogosEndpoints.Map(api);
        Ingesta.IngestaEndpoints.Map(api);
        return app;
    }
}
