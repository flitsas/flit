namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Puente hacia la configuración de consultas del tenant (HU #10478, Fase 5). Lee
/// <c>admin.tenant_operational_policies</c> (<c>consultation_provider_config</c> +
/// <c>runt_failover_timeout_ms</c>) y lo proyecta al <see cref="ConsultationTenantOverride"/> que
/// consume el chain resolver. La implementación vive en Infraestructura (Application no referencia
/// Admin ni EF). Devuelve <c>null</c> cuando el tenant no tiene fila (⇒ defaults globales).
/// </summary>
public interface IConsultationTenantOverrideProvider
{
    Task<ConsultationTenantOverride?> GetAsync(Guid tenantId, CancellationToken ct);
}
