using Flit.Modules.Security.Application.Auth.Login;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Modules.Security.Application;

public static class SecurityApplicationExtensions
{
    public static IServiceCollection AddSecurityApplication(this IServiceCollection services)
    {
        services.AddScoped<LoginHandler>();
        return services;
    }
}
