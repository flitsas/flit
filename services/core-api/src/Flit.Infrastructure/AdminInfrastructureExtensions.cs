using Flit.Admin.Domain.Companies;
using Flit.Admin.Domain.Companies.Settings;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Domain.Companies.VehicleOwnership;
using Flit.Admin.Domain.Companies.Whitelist;
using Flit.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Infrastructure;

/// <summary>
/// Registro de las dependencias de persistencia del módulo Admin (HU #10189, #10190, #10191).
/// </summary>
public static class AdminInfrastructureExtensions
{
    public static IServiceCollection AddAdminInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ICompanyReadRepository, CompanyReadRepository>();
        services.AddScoped<ICompanyWriteRepository, CompanyWriteRepository>();
        services.AddScoped<ITenantSettingsRepository, TenantSettingsRepository>();

        // HU #10191 — lista blanca + checker de propiedad vehicular (stub transitorio).
        services.AddScoped<IWhitelistRepository, WhitelistRepository>();
        services.AddScoped<IVehicleTenantOwnershipChecker, StubVehicleTenantOwnershipChecker>();

        // HU #10192 — grants de organismos de tránsito + consulta de audit log.
        services.AddScoped<ITransitGrantRepository, TransitGrantRepository>();
        services.AddScoped<ITenantAuditLogRepository, TenantAuditLogRepository>();

        return services;
    }
}
