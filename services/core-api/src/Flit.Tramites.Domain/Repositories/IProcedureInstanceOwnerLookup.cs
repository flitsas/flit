namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// HU #12358 (Feature #12257, épica #12235) — resuelve el cliente (tenant) DUEÑO de un trámite por su
/// identificador, sin filtro de tenant. Existe únicamente para el guard de escritura
/// (<c>TenantWriteGuard</c> en la API): una cabeza de grupo que manipule el identificador de un
/// trámite de un hijo debe recibir 403 por la comprobación de escritura (<c>TenantScope.CanWrite</c>),
/// nunca por la de lectura (AC4). NO es una lectura de negocio: no devuelve datos del trámite, solo el
/// id del dueño, y ningún endpoint lo expone.
/// </summary>
public interface IProcedureInstanceOwnerLookup
{
    /// <summary>Tenant dueño del trámite <paramref name="procedureInstanceId"/>, o <c>null</c> si no existe.</summary>
    Task<Guid?> GetOwnerTenantIdAsync(Guid procedureInstanceId, CancellationToken ct = default);

    /// <summary>Tenant dueño de cada trámite indicado (los ids inexistentes se omiten), en UNA consulta.</summary>
    Task<IReadOnlyDictionary<Guid, Guid>> GetOwnerTenantIdsAsync(
        IReadOnlyCollection<Guid> procedureInstanceIds, CancellationToken ct = default);
}
