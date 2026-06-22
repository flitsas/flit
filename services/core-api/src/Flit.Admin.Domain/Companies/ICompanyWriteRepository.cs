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

    /// <summary>
    /// Activa o desactiva la compañía (<c>identity.tenants.is_active</c>) y devuelve su
    /// proyección de listado actualizada, o <c>null</c> si el tenant no existe. Idempotente:
    /// si ya está en el estado pedido no escribe. Los triggers de BD registran la auditoría.
    /// </summary>
    Task<CompanyListItem?> SetActiveAsync(
        Guid tenantId,
        bool isActive,
        Guid? changedBy,
        CancellationToken cancellationToken = default);
}
