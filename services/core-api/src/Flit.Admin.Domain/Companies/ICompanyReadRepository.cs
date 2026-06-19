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
}
