namespace Flit.Comparendos.Api.Modulos;

/// <summary>Registro y mapeo de los módulos del producto. Cada módulo agrega aquí sus servicios y sus endpoints.</summary>
public static class Modulos
{
    public static IServiceCollection AddModulosComparendos(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return services;
    }

    public static WebApplication MapModulosComparendos(this WebApplication app, string prefijo)
    {
        var api = app.MapGroup(prefijo);
        api.MapGet("/sesion", Sesion.Obtener).RequireAuthorization(Plataforma.AutorizacionExtensions.PoliticaUsuario);
        return app;
    }
}
