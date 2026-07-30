using Flit.Admin.Domain.Common;

namespace Flit.Admin.Domain.Companies;

/// <summary>
/// Repositorio de solo lectura para el listado administrativo de compañías.
/// La implementación (Infrastructure) realiza la consulta paginada server-side
/// sobre <c>identity.tenants</c> con orden por fecha de creación descendente.
/// </summary>
public interface ICompanyReadRepository
{
    Task<PagedResult<CompanyListItem>> ListAsync(
        CompanyListFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// HU #11062 — una compañía por id, para que la consola de configuración pueda rotular EN TODO
    /// MOMENTO sobre qué compañía se está guardando. No sirve reutilizar la configuración operativa:
    /// esa devuelve 404 en una compañía sin parametrizar, que es justo cuando más importa saber
    /// dónde se está escribiendo. <c>null</c> si la compañía no existe.
    /// </summary>
    Task<CompanyListItem?> GetByIdAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
