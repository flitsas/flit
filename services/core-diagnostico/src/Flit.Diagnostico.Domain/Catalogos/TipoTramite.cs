using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Domain.Catalogos;

/// <summary>
/// Tipo de trámite publicado en Trámites que Diagnóstico ofrece en el modo Con Análisis IA (HU #13742). El
/// <see cref="Codigo"/> es la llave estable (por ejemplo <c>TRASPASO_STANDARD</c>); el nombre solo se muestra.
/// <see cref="FuentesObligatorias"/> llega vacía en D1 (R-17: el catálogo de reglas es de D6).
/// </summary>
public sealed record TipoTramite(string Codigo, string Nombre, string Familia, IReadOnlyList<Fuente> FuentesObligatorias);

/// <summary>Filtro de la etapa por código (lista blanca <c>Diagnostico:TiposTramite:Habilitados</c>), nunca por nombre.</summary>
public static class FiltroTiposTramite
{
    /// <summary>Los tipos publicados cuyo código está en <paramref name="codigosHabilitados"/>, en el orden de la publicación.</summary>
    public static IReadOnlyList<TipoTramite> Habilitados(IEnumerable<TipoTramite> publicados, IEnumerable<string> codigosHabilitados)
    {
        ArgumentNullException.ThrowIfNull(publicados);
        ArgumentNullException.ThrowIfNull(codigosHabilitados);
        var codigos = codigosHabilitados.Select(c => c.Trim()).Where(c => c.Length > 0).ToHashSet(StringComparer.Ordinal);
        return [.. publicados.Where(t => codigos.Contains(t.Codigo))];
    }
}
