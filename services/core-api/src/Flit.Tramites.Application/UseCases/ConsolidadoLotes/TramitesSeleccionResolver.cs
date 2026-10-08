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
        LoteSeleccion seleccion, LoteSeleccionContexto contexto, int? limite, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(seleccion);
        ArgumentNullException.ThrowIfNull(contexto);

        return seleccion switch
        {
            SeleccionPorIds porIds => await ResolverIdsAsync(porIds, contexto, limite, ct),
            SeleccionPorFiltro porFiltro => await ResolverFiltroAsync(porFiltro, contexto, limite, ct),
            _ => throw NoSoportado(seleccion),
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// Mismas validaciones que <see cref="ResolverAsync"/>. Modo ids: los ids de la compañía del token que existen
    /// (<c>COUNT</c> con <c>IdsIncluidos</c>). Modo filtro: <c>COUNT</c> del filtro del listado menos el <c>COUNT</c> de
    /// los excluidos que lo cumplen (mismo predicado + <c>IdsIncluidos</c> = excluidos), así un excluido ajeno al
    /// filtro no resta.
    /// </remarks>
    public async Task<int> ContarAsync(LoteSeleccion seleccion, LoteSeleccionContexto contexto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(seleccion);
        ArgumentNullException.ThrowIfNull(contexto);

        switch (seleccion)
        {
            case SeleccionPorIds porIds:
                {
                    var ids = IdsValidados(porIds);
                    return ids.Count == 0
                        ? 0
                        : await repo.CountIdsFilteredAsync(contexto.TenantId, new ProcedureInstanceListFilter { IdsIncluidos = ids }, ct);
                }

            case SeleccionPorFiltro porFiltro:
                {
                    var (request, excluidos) = FiltroValidado(porFiltro, contexto);
                    var total = await listado.CountIdsAsync(request, soloIds: null, ct);
                    if (excluidos.Count == 0 || total == 0)
                        return total;

                    var excluidosQueCumplen = await listado.CountIdsAsync(request, excluidos, ct);
                    return Math.Max(0, total - excluidosQueCumplen);
                }

            default:
                throw NoSoportado(seleccion);
        }
    }

    private async Task<IReadOnlyList<ProcedureInstanceRef>> ResolverIdsAsync(
        SeleccionPorIds seleccion, LoteSeleccionContexto contexto, int? limite, CancellationToken ct)
    {
        var ids = IdsValidados(seleccion);
        if (ids.Count == 0)
            return [];

        // Sin filtros del listado: solo la base (no borrados + tenant del token) ∩ ids, en el orden por defecto.
        var filter = new ProcedureInstanceListFilter { IdsIncluidos = ids };
        return await repo.ListIdsFilteredAsync(
            contexto.TenantId, filter, ProcedureInstanceSortBy.Default, SortDirection.Descending, limite, ct);
    }

    private async Task<IReadOnlyList<ProcedureInstanceRef>> ResolverFiltroAsync(
        SeleccionPorFiltro seleccion, LoteSeleccionContexto contexto, int? limite, CancellationToken ct)
    {
        var (request, excluidos) = FiltroValidado(seleccion, contexto);

        // Obs2: los excluidos se restan en memoria, así que se leen limite + excluidos: aunque todos caigan en el
        // prefijo leído, quedan al menos `limite` si la selección los tiene (contrato de ILoteSeleccionResolver).
        var refs = await listado.ResolveIdsAsync(request, limite is { } max ? max + excluidos.Count : null, ct);
        if (excluidos.Count == 0)
            return refs;

        var fuera = excluidos.ToHashSet();
        return refs.Where(r => !fuera.Contains(r.Id)).ToList();
    }

    /// <summary>Tope Q7 de la lista de ids y deduplicación.</summary>
    private static List<Guid> IdsValidados(SeleccionPorIds seleccion)
    {
        var ids = seleccion.Ids ?? [];
        if (ids.Count > LoteSeleccionTopes.MaxIds)
            throw new LoteSeleccionInvalidaException(
                LoteSeleccionInvalidaException.CodigoExcedeTope,
                $"La selección trae {ids.Count} trámites y el máximo es {LoteSeleccionTopes.MaxIds}. " +
                "Para descargar más, usa «Seleccionar todos» con un filtro.");

        return ids.Distinct().ToList();
    }

    /// <summary>Validaciones del modo filtro y la petición del listado con el tenant y el usuario del token.</summary>
    private static (ProcedureInstanceListRequest Request, IReadOnlyList<Guid> Excluidos) FiltroValidado(
        SeleccionPorFiltro seleccion, LoteSeleccionContexto contexto)
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

        return (request, excluidos);
    }

    private static ArgumentException NoSoportado(LoteSeleccion seleccion) =>
        new($"Modo de selección no soportado: {seleccion.GetType().Name}.", nameof(seleccion));
}
