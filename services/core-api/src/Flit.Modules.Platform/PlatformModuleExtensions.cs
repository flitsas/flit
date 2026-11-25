using Flit.Modules.Platform.Application.Access;
using Flit.Modules.Platform.Application.Apps;
using Flit.Modules.Platform.Application.Companies;
using Flit.Modules.Platform.Application.Manifest;
using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Modules.Security.Application.Products;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Modules.Platform;

/// <summary>
/// Registro de los casos de uso del módulo de plataforma (FLIT Suite, frente B). Los handlers se
/// inyectan por tipo concreto, sin MediatR, como el resto del repo.
/// </summary>
/// <remarks>
/// Los puertos (<c>IProductCatalog</c>, <c>ITenantProductRepository</c>) viven en
/// <c>Flit.Infrastructure</c> y los registra <c>AddPlatformInfrastructure()</c>.
/// </remarks>
public static class PlatformModuleExtensions
{
    public static IServiceCollection AddPlatformModule(this IServiceCollection services)
    {
        services.AddScoped<SetTenantProductEnabledHandler>();
        services.AddScoped<ListTenantProductsHandler>();
        // ADR-0092: API key del impuesto por empresa (escritura REST y entrega gRPC).
        services.AddScoped<SetTenantProductCredentialHandler>();
        services.AddScoped<DeleteTenantProductCredentialHandler>();
        services.AddScoped<GetTenantProductCredentialHandler>();
        // HU #14694: reintentar el aprovisionamiento y datos del alta en el proveedor (plan §1.2 R1 y §1.3 P1).
        services.AddScoped<RetryProvisioningHandler>();
        services.AddScoped<ObtenerDatosAltaProveedorHandler>();
        // HU #14694: estado de aprovisionamiento que reporta el producto y copia de la llave de Diagnóstico (D-CRED-DIAG).
        services.AddScoped<ReportProvisioningStatusHandler>();
        services.AddScoped<GetTenantProductCredentialForCopyHandler>();
        // HU #12965 (B-05): implementación real del contrato §4.
        services.AddScoped<IProductAccessResolver, ProductAccessResolver>();
        // Epic #13316 (HU #13334): productos encendidos de una empresa, para el gRPC de Identidad.
        services.AddScoped<ListEnabledProductsHandler>();
        // HU #12966 (B-06): me/apps y manifiesto. IProductHosts lo registra la API.
        services.AddScoped<ListMyAppsHandler>();
        services.AddScoped<ApplyProductManifestHandler>();
        return services;
    }
}
