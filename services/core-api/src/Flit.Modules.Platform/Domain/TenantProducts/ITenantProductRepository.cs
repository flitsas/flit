namespace Flit.Modules.Platform.Domain.TenantProducts;

/// <summary>
/// Persistencia de <c>platform.tenant_products</c>. Cada cambio deja su rastro legible en
/// <c>admin.tenant_config_audit_logs</c> dentro del mismo guardado.
/// </summary>
public interface ITenantProductRepository
{
    /// <summary>Las filas de la empresa (solo los productos que alguna vez se encendieron o apagaron).</summary>
    Task<IReadOnlyList<TenantProduct>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>La fila de la empresa para ese producto, o <c>null</c> si nunca se tocó.</summary>
    Task<TenantProduct?> GetAsync(Guid tenantId, string productCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea o actualiza la fila y audita cada campo que cambia (<c>enabled</c>, <c>notes</c>). Si nada
    /// cambia no escribe ni audita.
    /// </summary>
    Task<TenantProductChange> SetAsync(
        Guid tenantId,
        string productCode,
        bool enabled,
        string? notes,
        Guid? changedBy,
        CancellationToken cancellationToken = default);
}

/// <summary>Resultado de <see cref="ITenantProductRepository.SetAsync"/>.</summary>
/// <param name="Current">La fila tal como quedó.</param>
/// <param name="Changed"><c>true</c> si se escribió algo; <c>false</c> si ya estaba igual.</param>
public sealed record TenantProductChange(TenantProduct Current, bool Changed);
