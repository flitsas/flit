using Flit.Admin.Application.Companies.ListCompanies;
using Flit.Admin.Application.Companies.Settings.GetTenantSettings;
using Flit.Admin.Application.Companies.Settings.UpdateTenantSettings;
using Flit.Admin.Domain.Companies.Settings;
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

        // HU #10189 — listado de compañías.
        services.AddScoped<ListCompaniesHandler>();

        // HU #10190 — configuración operativa + audit log.
        services.AddScoped<GetTenantSettingsHandler>();
        services.AddScoped<UpdateTenantSettingsHandler>();
        services.AddSingleton<ITenantPolicyResolver, SnapshotTenantPolicyResolver>();

        return services;
    }
}
