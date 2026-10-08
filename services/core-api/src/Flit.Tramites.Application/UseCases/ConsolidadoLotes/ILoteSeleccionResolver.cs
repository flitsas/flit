using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>
/// Épica #13216 (HU #13370, ADR-0070 D3) — estrategia por origen que convierte la selección del usuario en
/// la lista congelada de trámites del lote, con la MISMA visibilidad del listado de ese origen.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var resolver = porOrigen.Para("tramites");
/// var refs = await resolver.ResolverAsync(
///     new SeleccionPorFiltro(new TramitesLoteFiltro(criterios), excluidos),
///     new LoteSeleccionContexto(tenantDelToken, usuarioDelToken), ct);
/// </code>
/// </remarks>
public interface ILoteSeleccionResolver
{
    /// <summary>Clave de origen del lote que atiende (<c>origin</c> de <c>consolidado_export_batches</c>).</summary>
    string Origen { get; }

    /// <summary>Trámites de la selección, en el orden del listado del origen.</summary>
    /// <param name="seleccion">Selección del cuerpo (ids o filtro con excluidos).</param>
    /// <param name="contexto">Tenant y usuario del token.</param>
    /// <param name="limite">
    /// Code review épica #13216 (Obs2) — corte de lectura para el tope M1. <c>null</c> = la selección entera. Con valor:
    /// si la selección (ya sin excluidos) tiene menos de <paramref name="limite"/> trámites se devuelve ENTERA; si tiene
    /// <paramref name="limite"/> o más, se devuelven AL MENOS <paramref name="limite"/> (un prefijo en el mismo orden) y
    /// el resto no se lee. Así «devolvió menos que el límite» garantiza selección completa.
    /// </param>
    /// <param name="ct">Cancelación.</param>
    /// <exception cref="LoteSeleccionInvalidaException">Tope superado o filtro inválido (422).</exception>
    Task<IReadOnlyList<ProcedureInstanceRef>> ResolverAsync(
        LoteSeleccion seleccion, LoteSeleccionContexto contexto, int? limite, CancellationToken ct = default);

    /// <summary>
    /// Code review épica #13216 (Obs2) — cuántos trámites devolvería <see cref="ResolverAsync"/> sin límite (excluidos
    /// ya restados), con el MISMO predicado contado en SQL. Solo lo pide el 422 <c>seleccion_excede_tope</c> para
    /// informar <c>total</c> sin cargar la selección.
    /// </summary>
    /// <exception cref="LoteSeleccionInvalidaException">Tope superado o filtro inválido (422).</exception>
    Task<int> ContarAsync(LoteSeleccion seleccion, LoteSeleccionContexto contexto, CancellationToken ct = default);
}

/// <summary>Atajos de <see cref="ILoteSeleccionResolver"/>.</summary>
public static class LoteSeleccionResolverExtensions
{
    /// <summary>La selección entera, sin límite (equivale a <c>ResolverAsync(seleccion, contexto, null, ct)</c>).</summary>
    public static Task<IReadOnlyList<ProcedureInstanceRef>> ResolverAsync(
        this ILoteSeleccionResolver resolver, LoteSeleccion seleccion, LoteSeleccionContexto contexto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        return resolver.ResolverAsync(seleccion, contexto, limite: null, ct);
    }
}

/// <summary>Elige el <see cref="ILoteSeleccionResolver"/> por la clave <c>origin</c> del lote.</summary>
public sealed class LoteSeleccionResolverPorOrigen
{
    private readonly Dictionary<string, ILoteSeleccionResolver> _porOrigen;

    public LoteSeleccionResolverPorOrigen(IEnumerable<ILoteSeleccionResolver> resolvers)
    {
        _porOrigen = new Dictionary<string, ILoteSeleccionResolver>(StringComparer.Ordinal);
        foreach (var resolver in resolvers)
        {
            if (!_porOrigen.TryAdd(resolver.Origen, resolver))
                throw new InvalidOperationException(
                    $"Hay dos resolvers de selección para el origen '{resolver.Origen}'.");
        }
    }

    /// <summary>
    /// HU #13374 — <c>true</c> si hay resolver para <paramref name="origen"/>. Punto de extensión del endpoint:
    /// el origen <c>superadmin</c> lo decide el servidor, pero su resolver llega con #13383; mientras tanto el
    /// endpoint responde 503 en vez de dejar que <see cref="Para"/> lance.
    /// </summary>
    public bool Atiende(string origen) => _porOrigen.ContainsKey(origen);

    /// <exception cref="InvalidOperationException">No hay resolver registrado para el origen.</exception>
    public ILoteSeleccionResolver Para(string origen) =>
        _porOrigen.TryGetValue(origen, out var resolver)
            ? resolver
            : throw new InvalidOperationException($"No hay resolver de selección para el origen '{origen}'.");
}
