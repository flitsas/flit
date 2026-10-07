using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>
/// Épica #13216 (HU #13370) — selección del lote desde el listado de <c>/tramites</c> (origen <c>tramites</c>).
/// No tiene lógica de visibilidad propia (ADR-0070 D3): el modo filtro delega en
/// <see cref="ListProcedureInstancesFilteredHandler.ResolveIdsAsync"/> (mismo filtro, búsqueda rápida y orden
/// que la tabla) y el modo ids en el mismo núcleo del repositorio con el filtro <c>IdsIncluidos</c>, acotado
/// al tenant del token. Un id de otra compañía, borrado o inexistente simplemente no aparece.
/// </summary>
public sealed class TramitesSeleccionResolver(
    ListProcedureInstancesFilteredHandler listado,
    IProcedureInstanceRepository repo) : ILoteSeleccionResolver
{
    /// <summary>Clave del origen: <see cref="ConsolidadoExportOrigin.Tramites"/> (DDL 133, #13367); un solo literal.</summary>
    public const string OrigenTramites = ConsolidadoExportOrigin.Tramites;

    public string Origen => OrigenTramites;

    public async Task<IReadOnlyList<ProcedureInstanceRef>> ResolverAsync(
        LoteSeleccion seleccion, LoteSeleccionContexto contexto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(seleccion);
        ArgumentNullException.ThrowIfNull(contexto);

        return seleccion switch
        {
            SeleccionPorIds porIds => await ResolverIdsAsync(porIds, contexto, ct),
            SeleccionPorFiltro porFiltro => await ResolverFiltroAsync(porFiltro, contexto, ct),
            _ => throw new ArgumentException(
                $"Modo de selección no soportado: {seleccion.GetType().Name}.", nameof(seleccion)),
        };
    }

    private async Task<IReadOnlyList<ProcedureInstanceRef>> ResolverIdsAsync(
        SeleccionPorIds seleccion, LoteSeleccionContexto contexto, CancellationToken ct)
    {
        var ids = seleccion.Ids ?? [];
        if (ids.Count > LoteSeleccionTopes.MaxIds)
            throw new LoteSeleccionInvalidaException(
                LoteSeleccionInvalidaException.CodigoExcedeTope,
                $"La selección trae {ids.Count} trámites y el máximo es {LoteSeleccionTopes.MaxIds}. " +
                "Para descargar más, usa «Seleccionar todos» con un filtro.");

        if (ids.Count == 0)
            return [];

        // Sin filtros del listado: solo la base (no borrados + tenant del token) ∩ ids, en el orden por defecto.
        var filter = new ProcedureInstanceListFilter { IdsIncluidos = ids.Distinct().ToList() };
        return await repo.ListIdsFilteredAsync(
            contexto.TenantId, filter, ProcedureInstanceSortBy.Default, SortDirection.Descending, ct);
    }

    private async Task<IReadOnlyList<ProcedureInstanceRef>> ResolverFiltroAsync(
        SeleccionPorFiltro seleccion, LoteSeleccionContexto contexto, CancellationToken ct)
    {
        if (seleccion.Filtro is not TramitesLoteFiltro filtro)
            throw new ArgumentException(
                $"El origen '{OrigenTramites}' no acepta el filtro {seleccion.Filtro?.GetType().Name ?? "null"}.",
                nameof(seleccion));

        var excluidos = seleccion.Excluidos ?? [];
        if (excluidos.Count > LoteSeleccionTopes.MaxExcluidos)
            throw new LoteSeleccionInvalidaException(
                LoteSeleccionInvalidaException.CodigoExcedeTope,
                $"La selección excluye {excluidos.Count} trámites y el máximo es {LoteSeleccionTopes.MaxExcluidos}. " +
                "Acota el filtro en lugar de desmarcar tantos.");

        // Mismas validaciones que el endpoint del listado: un campo o atajo fuera de catálogo no se ignora,
        // porque devolvería MÁS trámites de los que el usuario filtró.
        if (TramitesQueryConditions.Validate(filtro.Criterios.Condiciones) is { } problema)
            throw new LoteSeleccionInvalidaException(LoteSeleccionInvalidaException.CodigoFiltroInvalido, problema);
        if (BusquedaRapida.Validate(filtro.Criterios.BusquedaRapida) is { } atajoInvalido)
            throw new LoteSeleccionInvalidaException(LoteSeleccionInvalidaException.CodigoFiltroInvalido, atajoInvalido);

        // El tenant y el usuario salen del token, nunca del cuerpo.
        var request = filtro.Criterios with
        {
            TenantId = contexto.TenantId,
            UsuarioActualId = contexto.UsuarioActualId,
            Skip = 0,
        };

        var refs = await listado.ResolveIdsAsync(request, ct);
        if (excluidos.Count == 0)
            return refs;

        var fuera = excluidos.ToHashSet();
        return refs.Where(r => !fuera.Contains(r.Id)).ToList();
    }
}
