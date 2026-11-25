using Flit.Modules.Platform.Domain.TenantProducts;

namespace Flit.Modules.Platform.Application.TenantProducts;

/// <summary>Trabajos para el servicio de un producto con aprovisionamiento (plan §1.1, decisión E2).</summary>
public interface ITenantProductChangeJobs
{
    /// <summary>Encola <c>&lt;producto&gt;.producto.cambiado</c> en la outbox, sin guardar: viaja en el guardado del cambio.</summary>
    void EncolarCambio(TenantProduct actual);

    /// <summary>Encola <c>&lt;producto&gt;.aprovisionamiento.reintentar</c> y lo guarda.</summary>
    Task EncolarReintentoAsync(Guid tenantId, string productCode, Guid solicitadoPor, CancellationToken cancellationToken = default);
}
