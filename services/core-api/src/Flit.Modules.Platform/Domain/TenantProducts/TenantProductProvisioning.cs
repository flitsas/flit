namespace Flit.Modules.Platform.Domain.TenantProducts;

/// <summary>
/// HU #14694 (plan §1.2, decisión R1) — último estado de aprovisionamiento de un producto para una empresa, tal como lo
/// reportó el servicio del producto (<c>platform.tenant_product_provisioning</c>, DDL 205). Sin datos personales ni secretos.
/// </summary>
/// <param name="TenantId">Empresa.</param>
/// <param name="ProductCode">Producto con aprovisionamiento.</param>
/// <param name="Estado">Estado del plan §3.1 (p. ej. <c>LISTO</c>).</param>
/// <param name="MotivoCodigo">Código estable del motivo, o <c>null</c>.</param>
/// <param name="Mensaje">Motivo legible (hasta 300), o <c>null</c>.</param>
/// <param name="Accion">Acción del SuperAdmin, o <c>null</c>.</param>
/// <param name="FirmaConfirmada">Solo en <c>LISTO</c>; <c>null</c> en los demás estados.</param>
/// <param name="Version">Versión monotónica del servicio del producto.</param>
/// <param name="ActualizadoEn">Cuándo lo guardó la plataforma.</param>
public sealed record TenantProductProvisioning(
    Guid TenantId,
    string ProductCode,
    string Estado,
    string? MotivoCodigo,
    string? Mensaje,
    string? Accion,
    bool? FirmaConfirmada,
    long Version,
    DateTimeOffset ActualizadoEn);

/// <summary>Resultado de <see cref="ITenantProductProvisioningStore.GuardarSiEsMasNuevoAsync"/>.</summary>
/// <param name="EmpresaExiste"><c>false</c> si la empresa no existe (no se guarda nada).</param>
/// <param name="Aplicado"><c>true</c> si se guardó el reporte.</param>
/// <param name="VersionGuardada">Versión que queda guardada; 0 sin fila.</param>
public sealed record ProvisioningGuardado(bool EmpresaExiste, bool Aplicado, long VersionGuardada);

/// <summary>Persistencia de <c>platform.tenant_product_provisioning</c>.</summary>
public interface ITenantProductProvisioningStore
{
    /// <summary>Guarda el reporte solo si su versión es mayor que la guardada (atómico en la base).</summary>
    Task<ProvisioningGuardado> GuardarSiEsMasNuevoAsync(TenantProductProvisioning reporte, CancellationToken cancellationToken = default);

    /// <summary>Los estados guardados de la empresa.</summary>
    Task<IReadOnlyList<TenantProductProvisioning>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
