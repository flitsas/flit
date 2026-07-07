using Flit.Admin.Domain.Common;
using Flit.Admin.Domain.Companies.TransitOffices.Create;

namespace Flit.Admin.Domain.Companies.TransitOffices;

/// <summary>
/// Repositorio de alta y listado administrativo de tenants Organismo de Tránsito
/// (OT). A diferencia de <c>ICompanyWriteRepository</c>/<c>ICompanyReadRepository</c>
/// (separados para compañías), aquí se agrupan escritura y listado en una sola
/// interfaz porque el alta de OT es una operación compuesta (tenant + rol + perfil)
/// exclusiva de este flujo y no justifica, por ahora, un repositorio de solo lectura
/// aparte.
/// </summary>
public interface ITransitOfficeTenantWriteRepository
{
    /// <summary>Indica si ya existe un tenant con ese <c>code</c> (único en BD, tabla compartida con compañías).</summary>
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Indica si la oficina del catálogo ya tiene un <c>TransitOfficeProfile</c> asociado
    /// (regla de negocio nueva: una oficina física = un solo tenant OT).
    /// </summary>
    Task<bool> TransitOfficeHasProfileAsync(Guid transitOfficeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Da de alta el tenant OT: crea el <c>Tenant</c> (<c>TenantType = RENTING</c>), el
    /// rol de sistema <c>ot_admin</c> sin <c>RoleGrant</c>s, y el <c>TransitOfficeProfile</c>
    /// que lo vincula con la oficina del catálogo. El llamador (handler) ya validó los datos.
    /// </summary>
    Task<TransitOfficeTenantItem> CreateAsync(
        NewTransitOffice newTransitOffice,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lista paginada de tenants OT (join <c>identity.tenants</c> + <c>admin.transit_office_profiles</c>),
    /// para la pantalla de administración y para el selector de tenants OT del RBAC Admin.
    /// </summary>
    Task<PagedResult<TransitOfficeTenantItem>> ListAsync(
        TransitOfficeTenantListFilter filter,
        CancellationToken cancellationToken = default);
}
