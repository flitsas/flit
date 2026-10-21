namespace Flit.Admin.Domain.Companies.Settings;

/// <summary>
/// Habilitación de Trámites, Comparendos y Diagnóstico para una empresa, leída de <c>platform.tenant_products</c>
/// (HU #12967, B-07). Reemplaza a los booleans <c>tramites_module_enabled</c> y
/// <c>comparendos_module_enabled</c> de <c>admin.tenant_operational_policies</c>, que ya no se leen.
/// Lo implementa <c>Flit.Infrastructure</c>: Admin no referencia al módulo de plataforma (ciclo de proyectos).
/// </summary>
public interface ITenantProductFlags
{
    /// <summary>Sin fila en <c>platform.tenant_products</c>, el producto está apagado (fail-closed).</summary>
    Task<TenantProductFlags> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Trámites, Comparendos y Diagnóstico encendidos o apagados para la empresa. <paramref name="Diagnostico"/> se suma en la
/// HU #13726 (F00.1-H4) para que la suscripción de Diagnóstico se refleje igual que la de los otros dos.
/// </summary>
public sealed record TenantProductFlags(bool Tramites, bool Comparendos, bool Diagnostico = false);
