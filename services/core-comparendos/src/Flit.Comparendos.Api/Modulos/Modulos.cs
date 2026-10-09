namespace Flit.Comparendos.Api.Modulos;

/// <summary>Registro y mapeo de los módulos del producto. Cada módulo agrega aquí sus servicios y sus endpoints.</summary>
public static class Modulos
{
    public static IServiceCollection AddModulosComparendos(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddScoped<Comun.Auditoria>();
        services.AddScoped<Catalogos.MapeadorOt>();
        return services;
    }

    public static WebApplication MapModulosComparendos(this WebApplication app, string prefijo)
    {
        var api = app.MapGroup(prefijo);
        api.MapGet("/sesion", Sesion.Obtener).RequireAuthorization(Plataforma.AutorizacionExtensions.PoliticaUsuario);
        Monitoreo.MonitoreoEndpoints.Map(api);
        Catalogos.CatalogosEndpoints.Map(api);
        return app;
    }
}
