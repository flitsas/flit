using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Domain.Diagnosticos;

/// <summary>
/// Alcance de un diagnóstico tal como lo declara el usuario, en sus tres grupos (HU #13750, F01-H4). La cabecera lo guarda
/// plano (<see cref="Fuentes"/>, columna <c>alcance</c>) y se vuelve a agrupar con <see cref="Agrupar"/> al leerlo. Que cada
/// fuente esté en su grupo lo verifica <see cref="ValidadorCabecera"/>, no este tipo.
/// </summary>
public sealed record Alcance(IReadOnlyList<Fuente> PorVehiculo, IReadOnlyList<Fuente> PorPropietario, IReadOnlyList<Fuente> PorLocatario)
{
    /// <summary>Todas las fuentes, sin repetir, en el orden de los grupos.</summary>
    public IReadOnlyList<Fuente> Fuentes => [.. PorVehiculo.Concat(PorPropietario).Concat(PorLocatario).Distinct()];

    /// <summary>Alcance agrupado de una lista plana de fuentes (cada una en su grupo, sin repetir).</summary>
    public static Alcance Agrupar(IEnumerable<Fuente> fuentes)
    {
        ArgumentNullException.ThrowIfNull(fuentes);
        var distintas = fuentes.Distinct().ToList();
        return new(Del(distintas, GrupoFuente.PorVehiculo), Del(distintas, GrupoFuente.PorPropietario), Del(distintas, GrupoFuente.PorLocatario));
    }

    private static Fuente[] Del(IEnumerable<Fuente> fuentes, GrupoFuente grupo) => [.. fuentes.Where(f => GruposFuente.De(f) == grupo)];
}
