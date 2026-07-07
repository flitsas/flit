namespace Flit.Admin.Domain.Companies.MandateSigners;

/// <summary>
/// Lecturas cross-tenant de mandatarios y de las compañías gestoras de un organismo de
/// tránsito (ADR-0023). Es una lectura de administración OT (SuperAdmin / ot_admin) que por
/// definición atraviesa varios tenants gestores, así que corre con
/// <c>SET LOCAL row_security = off</c> (mismo patrón que
/// <see cref="ITransitOfficeOperationalStatusReader"/>).
/// </summary>
public interface IMandateSignerReader
{
    /// <summary>
    /// Mandatarios <b>activos</b> del OT con sus compañías asignadas, ordenados por nombre.
    /// Los mandatarios inactivados (baja lógica) no aparecen y sus compañías quedan libres.
    /// </summary>
    Task<IReadOnlyList<MandateSignerItem>> ListActiveByOtAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Un mandatario por id (cualquier estado), con sus compañías activas. <c>null</c> si no
    /// existe. Usado para precargar el formulario de edición.
    /// </summary>
    Task<MandateSignerItem?> GetByIdAsync(
        Guid mandateSignerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compañías gestoras con grant en el OT (candidatas a mandatario), con su estado activo
    /// y si el grant está habilitado. Insumo del multiselect y de la regla de uso RF33.
    /// </summary>
    Task<IReadOnlyList<OtCompanyOption>> ListOtCompaniesAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Por cada compañía con un mandatario activo asignado en el OT, el mandatario que la
    /// firma (id, nombre, huella). Base de la exclusividad (compañías ya tomadas) y de la
    /// vista consolidada RF34.
    /// </summary>
    Task<IReadOnlyList<MandateSignerCompanyResolution>> ListActiveCompanyResolutionsAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken = default);
}
