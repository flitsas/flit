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
/// <para>HU #13417 (adenda v7) — con <see cref="LoteSeleccionContexto.Alcance"/> (lote desde la vista de red) el modo
/// filtro delega en <see cref="NetworkListProcedureInstancesHandler.ResolveIdsAsync"/> (el universo de
/// <c>POST /network/instances/search</c>) y el modo ids va contra
/// <see cref="IProcedureInstanceRepository.ListIdsFilteredInScopeAsync"/>: los ids del cuerpo ∩ el alcance de red en
/// SQL (<c>WhereTenantInScope</c>). El caso de uso vuelve a filtrar por el alcance al congelar (defensa doble).</para>
/// </summary>
/// <param name="listado">Listado de <c>/tramites</c> (alcance propio).</param>
/// <param name="repo">Repositorio de trámites.</param>
/// <param name="red">Listado de la red (HU #13417); <c>null</c> = se construye sobre <paramref name="repo"/>.</param>
public sealed class TramitesSeleccionResolver(
    ListProcedureInstancesFilteredHandler listado,
    IProcedureInstanceRepository repo,
    NetworkListProcedureInstancesHandler? red = null) : ILoteSeleccionResolver
{
    private readonly NetworkListProcedureInstancesHandler _red = red ?? new NetworkListProcedureInstancesHandler(repo);

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
                    if (ids.Count == 0)
                        return 0;
                    var filter = new ProcedureInstanceListFilter { IdsIncluidos = ids };
                    return AlcanceDeRed(contexto) is { } alcance
                        ? await repo.CountIdsFilteredInScopeAsync(alcance, filter, ct)
                        : await repo.CountIdsFilteredAsync(contexto.TenantId, filter, ct);
                }

            case SeleccionPorFiltro porFiltro:
                {
                    var (request, excluidos) = FiltroValidado(porFiltro, contexto);
                    var alcance = AlcanceDeRed(contexto);
                    var total = await ContarFiltroAsync(alcance, request, soloIds: null, ct);
                    if (excluidos.Count == 0 || total == 0)
                        return total;

                    var excluidosQueCumplen = await ContarFiltroAsync(alcance, request, excluidos, ct);
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

        // Sin filtros del listado: solo la base (no borrados + tenant del token, o alcance de red) ∩ ids, en el orden
        // por defecto. AC5 #13417: un id de una compañía fuera del alcance no sale de SQL.
        var filter = new ProcedureInstanceListFilter { IdsIncluidos = ids };
        return AlcanceDeRed(contexto) is { } alcance
            ? await repo.ListIdsFilteredInScopeAsync(
                alcance, filter, ProcedureInstanceSortBy.Default, SortDirection.Descending, limite, ct)
            : await repo.ListIdsFilteredAsync(
                contexto.TenantId, filter, ProcedureInstanceSortBy.Default, SortDirection.Descending, limite, ct);
    }

    private async Task<IReadOnlyList<ProcedureInstanceRef>> ResolverFiltroAsync(
        SeleccionPorFiltro seleccion, LoteSeleccionContexto contexto, int? limite, CancellationToken ct)
    {
        var (request, excluidos) = FiltroValidado(seleccion, contexto);

        // Obs2: los excluidos se restan en memoria, así que se leen limite + excluidos: aunque todos caigan en el
        // prefijo leído, quedan al menos `limite` si la selección los tiene (contrato de ILoteSeleccionResolver).
        var leer = limite is { } max ? max + excluidos.Count : (int?)null;
        var refs = AlcanceDeRed(contexto) is { } alcance
            ? await _red.ResolveIdsAsync(alcance, request, leer, ct)
            : await listado.ResolveIdsAsync(request, leer, ct);
        if (excluidos.Count == 0)
            return refs;

        var fuera = excluidos.ToHashSet();
        return refs.Where(r => !fuera.Contains(r.Id)).ToList();
    }

    private Task<int> ContarFiltroAsync(
        Flit.Queries.Domain.Tenancy.TenantScope? alcance, ProcedureInstanceListRequest request,
        IReadOnlyCollection<Guid>? soloIds, CancellationToken ct) =>
        alcance is not null
            ? _red.CountIdsAsync(alcance, request, soloIds, ct)
            : listado.CountIdsAsync(request, soloIds, ct);

    /// <summary>
    /// HU #13417 — alcance de red del contexto, o <c>null</c> (propio). Nunca el alcance total: el Super Admin tiene su
    /// propio resolver y la vista de red no le aplica.
    /// </summary>
    private static Flit.Queries.Domain.Tenancy.TenantScope? AlcanceDeRed(LoteSeleccionContexto contexto) =>
        contexto.Alcance switch
        {
            null => null,
            { IsAll: true } => throw new ArgumentException(
                "El lote de trámites nunca se resuelve con el alcance total.", nameof(contexto)),
            var alcance => alcance,
        };

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
