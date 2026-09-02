using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// Persistencia de las marcas de firma a posteriori (HU #11196). Todo tenant-scoped: el aislamiento por
/// tenant es lo que garantiza el AC5 (los trámites marcados de otra empresa gestora no entran al lote).
/// </summary>
public interface IDeferredSignatureMarkRepository
{
    /// <summary>
    /// Marca pendiente de un trámite y parte, o <c>null</c> si no la tiene.
    /// </summary>
    /// <param name="representativeDocumentNumber">
    /// ADR-0053 (Múltiple Propietario) — documento del representante legal AL QUE se refiere la marca.
    /// Antes de ADR-0053 un rol (<paramref name="partyRole"/>) admitía un solo actor jurídico, así que
    /// <c>(tenant, trámite, rol)</c> ya era una llave única; con 2..4 actores jurídicos por rol, dos
    /// copropietarios pueden necesitar CADA UNO su propia marca pendiente, y sin este parámetro la
    /// consulta devolvía indistintamente la primera que encontrara — la marca de un copropietario se
    /// pisaba con la del otro. Opcional/aditivo: <c>null</c> (el mandatario, que no es un actor del
    /// trámite y siempre es único por trámite; o un llamador legado) preserva la búsqueda solo por rol.
    /// </param>
    Task<DeferredSignatureMark?> FindPendienteAsync(
        Guid tenantId, Guid procedureInstanceId, string partyRole, string? representativeDocumentNumber = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Todas las marcas PENDIENTES de una persona (por documento) en el tenant, en orden determinista de
    /// creación: es el lote que se firma cuando esa persona valida su identidad.
    /// </summary>
    Task<IReadOnlyList<DeferredSignatureMark>> ListPendientesByRepresentativeAsync(
        Guid tenantId,
        string representativeDocumentType,
        string representativeDocumentNumber,
        CancellationToken cancellationToken = default);

    void Add(DeferredSignatureMark mark);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
