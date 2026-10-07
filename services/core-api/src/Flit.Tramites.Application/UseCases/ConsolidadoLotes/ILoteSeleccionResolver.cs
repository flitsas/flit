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
    /// <exception cref="LoteSeleccionInvalidaException">Tope superado o filtro inválido (422).</exception>
    Task<IReadOnlyList<ProcedureInstanceRef>> ResolverAsync(
        LoteSeleccion seleccion, LoteSeleccionContexto contexto, CancellationToken ct = default);
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

    /// <exception cref="InvalidOperationException">No hay resolver registrado para el origen.</exception>
    public ILoteSeleccionResolver Para(string origen) =>
        _porOrigen.TryGetValue(origen, out var resolver)
            ? resolver
            : throw new InvalidOperationException($"No hay resolver de selección para el origen '{origen}'.");
}
