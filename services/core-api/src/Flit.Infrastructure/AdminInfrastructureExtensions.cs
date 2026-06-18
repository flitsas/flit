using Flit.Admin.Domain.Companies;
using Flit.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Infrastructure;

/// <summary>
/// Registro de las dependencias de persistencia del módulo Admin (HU #10189).
/// </summary>
public static class AdminInfrastructureExtensions
{
    public static IServiceCollection AddAdminInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ICompanyReadRepository, CompanyReadRepository>();

        return services;
    }
}
