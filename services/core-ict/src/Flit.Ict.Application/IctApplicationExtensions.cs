using Flit.Ict.Application.Auth.Login;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Ict.Application;

/// <summary>Registro de los casos de uso (handlers POCO) de la capa Application de ICT.</summary>
public static class IctApplicationExtensions
{
    public static IServiceCollection AddIctApplication(this IServiceCollection services)
    {
        services.AddScoped<LoginIntegrationClientHandler>();
        return services;
    }
}
