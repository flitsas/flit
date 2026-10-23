using Flit.Tramites.Domain.Enums;

namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// HU #13664 (Feature #13636 F-F, Épica #12753) — familia (<see cref="ProcedureFamily"/>) de un trámite por su id, en una
/// consulta liviana (solo el código de familia del tipo, sin grafo). La usa el guard de «Ver por familia» de la API para
/// decidir el 403 de TODAS las rutas por id del trámite en un solo punto. No devuelve datos del trámite.
/// <para>Uso de ejemplo: <c>await lookup.GetFamilyAsync(id, scope.ReadTenantIds)</c> → <c>ProcedureFamily.Traspaso</c>, o <c>null</c> si no existe en el alcance.</para>
/// </summary>
public interface IProcedureInstanceFamilyLookup
{
    /// <summary>
    /// Familia del trámite <paramref name="procedureInstanceId"/>, o <c>null</c> si no existe (o no pertenece a
    /// <paramref name="tenantIds"/>: el handler responderá su 404 de siempre). <c>tenantIds == null</c> = sin filtro (SuperAdmin).
    /// </summary>
    Task<ProcedureFamily?> GetFamilyAsync(
        Guid procedureInstanceId, IReadOnlyCollection<Guid>? tenantIds, CancellationToken ct = default);

    /// <summary>Familia de cada trámite indicado (los inexistentes o fuera de <paramref name="tenantIds"/> se omiten), en UNA consulta: para rutas con ids en el body.</summary>
    Task<IReadOnlyDictionary<Guid, ProcedureFamily>> GetFamiliesAsync(
        IReadOnlyCollection<Guid> procedureInstanceIds, IReadOnlyCollection<Guid>? tenantIds, CancellationToken ct = default);
}
