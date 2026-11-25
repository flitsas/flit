namespace Flit.Modules.Platform.Application.TenantProducts;

/// <summary>Precondición para encender un producto con aprovisionamiento (D-EMAIL).</summary>
public interface IPrecondicionActivacion
{
    /// <summary><c>true</c> si la compañía cumple la precondición.</summary>
    Task<bool> CumpleAsync(Guid tenantId, string productCode, CancellationToken cancellationToken = default);
}
