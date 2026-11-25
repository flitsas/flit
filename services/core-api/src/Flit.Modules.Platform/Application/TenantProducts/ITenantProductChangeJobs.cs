using Flit.Modules.Platform.Domain.TenantProducts;

namespace Flit.Modules.Platform.Application.TenantProducts;

/// <summary>Trabajos para el servicio de un producto con aprovisionamiento (plan §1.1, decisión E2).</summary>
public interface ITenantProductChangeJobs
{
    /// <summary>Encola <c>&lt;producto&gt;.producto.cambiado</c> en la outbox, sin guardar: viaja en el guardado del cambio.</summary>
    void EncolarCambio(TenantProduct actual);

    /// <summary>
    /// HU #14711 — encola (sin guardar) el trabajo <c>impuestos.credencial_diagnostico.actualizada</c> v1
    /// <c>{tenantId, productCode:"diagnostico", version, occurredAt}</c> para que Impuestos recopie la llave. Nunca lleva la llave.
    /// </summary>
    void EncolarCredencialDiagnosticoActualizada(Guid tenantId, long version, DateTimeOffset occurredAt);

    /// <summary>Encola <c>&lt;producto&gt;.aprovisionamiento.reintentar</c> y lo guarda.</summary>
    Task EncolarReintentoAsync(Guid tenantId, string productCode, Guid solicitadoPor, CancellationToken cancellationToken = default);
}
