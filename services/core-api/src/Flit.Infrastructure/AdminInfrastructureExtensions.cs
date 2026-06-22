using Flit.Admin.Domain.Companies;
using Flit.Admin.Domain.Companies.Settings;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Domain.Companies.VehicleOwnership;
using Flit.Admin.Domain.Companies.Whitelist;
using Flit.Admin.Domain.DocumentOrderOverrides;
using Flit.Admin.Domain.DocumentRequirementOverrides;
using Flit.Admin.Domain.DocumentRequirements;
using Flit.Admin.Domain.DocumentTypes;
using Flit.Admin.Domain.ProcedureSnapshots;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Services;
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

        // HU #10193 — catálogo de tipos de documento (CRUD SuperAdmin).
        services.AddScoped<IDocumentTypeRepository, DocumentTypeRepository>();

        // HU #10195 — asociación documentos ↔ tipos de trámite + catálogo de trámites
        // (read-only). El guard de uso es ahora la implementación real (HU #10197).
        services.AddScoped<IProcedureDocumentRequirementRepository, ProcedureDocumentRequirementRepository>();
        services.AddScoped<IProcedureTypeCatalog, ProcedureTypeCatalog>();
        services.AddScoped<IProcedureDocumentRequirementUsageGuard, ProcedureDocumentRequirementUsageGuard>();

        // HU #10196 — overrides de orden documental (OT/Cliente) + catálogo de clientes
        // (read-only) + resolutor de la matriz documental (precedencia Cliente > OT > Default).
        services.AddScoped<IDocumentOrderOverrideRepository, DocumentOrderOverrideRepository>();
        services.AddScoped<ITenantCatalog, TenantCatalog>();
        services.AddScoped<IResolvedDocumentMatrixResolver, ResolvedDocumentMatrixResolver>();

        // HU #10198 — overrides de obligatoriedad documental por OT (3 estados).
        services.AddScoped<IDocumentRequirementOverrideRepository, DocumentRequirementOverrideRepository>();

        // HU #10197 — instancias de trámite + snapshot documental inmutable.
        services.AddScoped<IProcedureInstanceRepository, ProcedureInstanceRepository>();
        services.AddScoped<IProcedureDocumentSnapshotRepository, ProcedureDocumentSnapshotRepository>();

        return services;
    }
}
