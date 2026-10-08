using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>
/// Épica #13216 (HU #13383, ADR-0070 A4.1) — selección del lote del Super Admin (origen <c>superadmin</c>). Clase
/// delgada SIN lógica propia: delega entera en <see cref="TramitesSeleccionResolver"/> (mismo filtro, búsqueda rápida,
/// orden, topes y validación de catálogo que el listado de <c>/tramites</c>), porque la tabla del Super Admin es la
/// misma. Lo único que cambia es el tenant del contexto, que pone el caso de uso: el scope de <c>X-Tenant-Id</c>, o
/// <c>null</c> = todas las compañías (<c>ListIdsFilteredAsync(Guid?)</c>, W-g). La condición <c>compania</c> del filtro
/// la aplica el propio listado (W-e).
/// </summary>
/// <remarks>
/// <para>Por qué clase delgada y no alias: <see cref="ILoteSeleccionResolver"/> atiende un solo
/// <see cref="ILoteSeleccionResolver.Origen"/> por instancia y <see cref="TramitesSeleccionResolver"/> lo fija en
/// <c>tramites</c>. Un alias (registro por fábrica que reetiquete el origen) dejaría el origen <c>superadmin</c> sin tipo
/// propio en el contenedor, en los snapshots de DI y en las trazas; parametrizar el origen en el constructor de #13370
/// cambiaría su firma para todos sus consumidores. Esta clase compone el resolver de <c>tramites</c>, así que no hay
/// copia de la visibilidad que pueda divergir.</para>
/// <para>Uso de ejemplo:
/// <code>
/// var refs = await porOrigen.Para("superadmin").ResolverAsync(
///     seleccion, new LoteSeleccionContexto(scopeTenantIdONull, superAdminId), ct);
/// </code></para>
/// </remarks>
public sealed class SuperAdminSeleccionResolver(
    ListProcedureInstancesFilteredHandler listado,
    IProcedureInstanceRepository repo) : ILoteSeleccionResolver
{
    private readonly TramitesSeleccionResolver _tramites = new(listado, repo);

    public string Origen => ConsolidadoExportOrigin.Superadmin;

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="LoteSeleccionContexto.TenantId"/> es el scope del Super Admin (<c>null</c> = todas). La defensa
    /// «solo entra el scope» sobre el resultado la repite <see cref="CrearLoteConsolidadosHandler"/> al congelar.
    /// </remarks>
    public Task<IReadOnlyList<ProcedureInstanceRef>> ResolverAsync(
        LoteSeleccion seleccion, LoteSeleccionContexto contexto, int? limite, CancellationToken ct = default) =>
        _tramites.ResolverAsync(seleccion, contexto, limite, ct);

    /// <inheritdoc />
    public Task<int> ContarAsync(LoteSeleccion seleccion, LoteSeleccionContexto contexto, CancellationToken ct = default) =>
        _tramites.ContarAsync(seleccion, contexto, ct);
}
