using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// Proveedor de los parámetros documentales de la compañía gestora (RF31) para el cómputo del
/// checklist. Desacopla el motor de trámites del submódulo Admin: la implementación (en
/// Infraestructura) mapea la persistencia de <c>admin.company_document_params</c> a los VO del
/// dominio de trámites. Sin parámetros configurados devuelve una lista vacía ⇒ checklist sin cambios.
/// </summary>
public interface IChecklistCompanyParamsProvider
{
    /// <summary>Parámetros documentales de la gestora <paramref name="tenantId"/> (posiblemente vacío).</summary>
    Task<IReadOnlyList<CompanyDocumentParam>> GetForTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
