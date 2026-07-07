namespace Flit.Admin.Domain.Companies.TransitOffices;

/// <summary>
/// Lector cross-tenant del estado operativo de los organismos de tránsito (RF01):
/// catálogo activo LEFT JOIN perfil OT + tenant. Es una lectura de SuperAdmin (por
/// definición atraviesa todos los tenants), análoga a
/// <see cref="ITransitOfficeTenantWriteRepository.ListAsync"/>.
/// </summary>
public interface ITransitOfficeOperationalStatusReader
{
    /// <summary>
    /// Devuelve una fila por cada oficina activa del catálogo, indicando si tiene tenant
    /// OT y —si lo tiene— su estado activo y modo de operación. Ordenado por nombre.
    /// </summary>
    Task<IReadOnlyList<TransitOfficeOperationalStatusItem>> ListAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Estado operativo de una sola oficina del catálogo (HU #10518, validación del grant).
    /// Mismo join que <see cref="ListAsync"/> acotado a <paramref name="transitOfficeId"/>.
    /// Devuelve <c>null</c> si la oficina no existe o no está activa en el catálogo.
    /// </summary>
    Task<TransitOfficeOperationalStatusItem?> GetByIdAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken = default);
}
