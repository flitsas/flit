using Flit.DrFlit.Application.Chat;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.DrFlit.Application;

/// <summary>
/// Registro DI de la capa Application de DR. FLIT (Épica #12718). Los puertos (modelo, contador, manual,
/// ajustes) los registra Infrastructure en <c>AddPostgresInfrastructure</c>.
/// </summary>
public static class DrFlitApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddDrFlitApplication(this IServiceCollection services)
    {
        services.AddScoped<IDrFlitAssistant, DrFlitAssistant>();
        return services;
    }
}
