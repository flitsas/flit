using Flit.Admin.Application.Companies.ListCompanies;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Admin.Application;

/// <summary>
/// Registro de los casos de uso del módulo Admin en el contenedor DI.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddAdminApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ListCompaniesHandler>();

        return services;
    }
}
