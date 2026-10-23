namespace Flit.Admin.Domain.Companies.Settings;

/// <summary>
/// Habilitación de Trámites y Comparendos para una empresa, leída de <c>platform.tenant_products</c>
/// (HU #12967, B-07). Reemplaza a los booleans <c>tramites_module_enabled</c> y
/// <c>comparendos_module_enabled</c> de <c>admin.tenant_operational_policies</c>, que ya no se leen.
/// Lo implementa <c>Flit.Infrastructure</c>: Admin no referencia al módulo de plataforma (ciclo de proyectos).
/// </summary>
public interface ITenantProductFlags
{
    /// <summary>Sin fila en <c>platform.tenant_products</c>, el producto está apagado (fail-closed).</summary>
    Task<TenantProductFlags> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Trámites y Comparendos encendidos o apagados para la empresa.</summary>
public sealed record TenantProductFlags(bool Tramites, bool Comparendos);
