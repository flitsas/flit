using Flit.Admin.Domain.Companies.Create;

namespace Flit.Admin.Domain.Companies;

/// <summary>
/// Repositorio de escritura para el alta de compañías sobre <c>identity.tenants</c>.
/// La tabla no tiene RLS; los triggers de BD generan row_version y auditoría.
/// </summary>
public interface ICompanyWriteRepository
{
    /// <summary>Indica si ya existe una compañía con ese <c>code</c> (único en BD).</summary>
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserta la compañía y devuelve su proyección de listado (incluye el id y la
    /// fecha de creación generados). El llamador (handler) ya validó los datos.
    /// </summary>
    Task<CompanyListItem> CreateAsync(NewCompany company, CancellationToken cancellationToken = default);
}
