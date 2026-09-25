using Flit.Admin.Domain.Companies.Settings;

namespace Flit.Admin.Tests.Companies.Settings;

/// <summary>
/// HU #12967 (B-07) — habilitación fija de Trámites y Comparendos para las pruebas de configuración. La lectura
/// real de <c>platform.tenant_products</c> la prueban las de integración contra Postgres.
/// </summary>
internal sealed class StubTenantProductFlags(bool tramites = true, bool comparendos = false) : ITenantProductFlags
{
    public Task<TenantProductFlags> GetAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new TenantProductFlags(tramites, comparendos));
}
