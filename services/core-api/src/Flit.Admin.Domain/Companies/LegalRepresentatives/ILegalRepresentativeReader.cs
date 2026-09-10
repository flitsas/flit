using Flit.Admin.Domain.Common;

namespace Flit.Admin.Domain.Companies.LegalRepresentatives;

/// <summary>
/// Lecturas del directorio de representantes legales por compañía (HU #10900, ADR-0033). Tenant-
/// scoped: cada lectura corre bajo el contexto RLS del tenant (<c>app.current_tenant_id</c>).
/// <c>DocumentNumber</c> es PII (Ley 1581): no loguear.
/// </summary>
public interface ILegalRepresentativeReader
{
    /// <summary>Listado paginado de representantes del tenant (compañía + firma/identidad).</summary>
    Task<PagedResult<LegalRepresentativeItem>> ListPagedAsync(
        Guid tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Un representante por id dentro del tenant. <c>null</c> si no existe.</summary>
    Task<LegalRepresentativeItem?> GetByIdAsync(
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Representante ACTIVO del tenant por NIT de la compañía + documento del representante. Base de
    /// la precarga por NIT del wizard (evita RUNT/RUES). <c>null</c> si no hay match.
    /// </summary>
    Task<LegalRepresentativeItem?> FindActiveByCompanyNitAndDocumentAsync(
        Guid tenantId,
        string companyNit,
        string documentType,
        string documentNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Representante ACTIVO más reciente del tenant por NIT de la compañía (HU #10903). Base del
    /// lookup por NIT del wizard cuando solo se conoce el NIT ingresado: si hay match, el FE precarga
    /// comprador/vendedor y NO consulta RUNT/RUES. <c>null</c> si el tenant no tiene un representante
    /// activo para ese NIT.
    /// </summary>
    Task<LegalRepresentativeItem?> FindActiveByCompanyNitAsync(
        Guid tenantId,
        string companyNit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// TODOS los representantes ACTIVOS del tenant por NIT de la compañía (HU #10932/#10937). Base del
    /// selector cuando una compañía tiene más de un representante en el wizard. Cruza por el puente
    /// <c>legal_representative_companies</c> (multiempresa). Lista vacía si no hay match.
    /// </summary>
    Task<IReadOnlyList<LegalRepresentativeItem>> ListActiveByCompanyNitAsync(
        Guid tenantId,
        string companyNit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Representante ACTIVO del tenant por documento de la persona (HU #10932). Base del "se crea una
    /// sola vez": si ya existe la persona, el guardado le agrega compañías en vez de duplicarla.
    /// <c>null</c> si no hay match.
    /// </summary>
    Task<LegalRepresentativeItem?> FindActiveByDocumentAsync(
        Guid tenantId,
        string documentType,
        string documentNumber,
        CancellationToken cancellationToken = default);

    /// <summary>Compañías representadas del tenant (alimenta el multi-select de escrituras).</summary>
    Task<IReadOnlyList<RepresentedCompanyItem>> ListRepresentedCompaniesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compañía activa del tenant por NIT. Si hay varias fichas (un NIT por RL), <c>null</c> —
    /// hay que resolver por representante.
    /// </summary>
    Task<RepresentedCompanyItem?> FindRepresentedCompanyByNitAsync(
        Guid tenantId,
        string documentNumber,
        CancellationToken cancellationToken = default);

    /// <summary>Ficha activa de un NIT dueña de un representante concreto.</summary>
    Task<RepresentedCompanyItem?> FindActiveCompanyForRepresentativeAsync(
        Guid tenantId,
        Guid representativeId,
        string documentNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Proyección ligera (nombre + documento) de representantes por id — consumo del wizard al listar
    /// escrituras vigentes. Solo ids del tenant; omite ids inexistentes. Diccionario vacío si
    /// <paramref name="ids"/> está vacío.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, LegalRepresentativeBrief>> FindBriefByIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default);
}
