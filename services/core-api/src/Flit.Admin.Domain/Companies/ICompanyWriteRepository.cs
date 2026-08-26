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
    /// ¿Ya existe una compañía con ese NIT? El NIT identifica a la empresa ante el Estado, así que dos
    /// tenants con el mismo NIT son la misma empresa duplicada: aparecen dos veces —y con la misma razón
    /// social— en cualquier listado que las ofrezca, sin forma de distinguirlas.
    /// </summary>
    Task<bool> TaxIdExistsAsync(string taxId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve la proyección de listado de la compañía, o <c>null</c> si no existe.
    /// La usa el handler de edición para resolver/validar el <c>tenant_type</c> respecto
    /// del valor actual: preserva tipos heredados fuera del catálogo B2B
    /// (p.ej. <c>standard</c>/<c>transit_office</c>) cuando la edición no los cambia.
    /// </summary>
    Task<CompanyListItem?> GetByIdAsync(Guid tenantId, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Actualiza los datos editables de la compañía (razón social, NIT, tipo y estado)
    /// sobre <c>identity.tenants</c> y devuelve su proyección de listado actualizada, o
    /// <c>null</c> si el tenant no existe. El <c>code</c> es inmutable (no se toca).
    /// Idempotente: solo persiste si hay cambios reales. El llamador (handler) ya validó.
    /// </summary>
    Task<CompanyListItem?> UpdateAsync(
        Guid tenantId,
        string legalName,
        string taxId,
        string tenantType,
        bool isActive,
        Guid? changedBy,
        CancellationToken cancellationToken = default);
}
