using Flit.Queries.Domain;
using Flit.Tramites.Domain.Enums;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Opciones del campo «tipo de trámite» de los dos motores de consultas, agrupadas por familia.
/// </summary>
/// <remarks>
/// <para>
/// Vive aquí, y no en cada repositorio, porque el organismo y la empresa preguntan lo mismo y tienen
/// que ofrecer lo mismo: dos listas que se parecen es como se llega a que una ofrezca «Blindaje» y
/// la otra no.
/// </para>
/// <para>
/// Cada familia con trámites aporta una opción «Toda la familia» —cuyo valor es el código de la
/// familia— seguida de sus tipos concretos. Que ambos niveles convivan en el MISMO campo es lo que
/// permite responder «cuántas matrículas» y «cuántas matrículas de leasing» sin dos filtros
/// distintos, y lo que mantiene válidas las consultas guardadas de cuando la única opción era la
/// familia.
/// </para>
/// </remarks>
internal static class TipoTramiteOptionCatalog
{
    /// <summary>
    /// Familias en orden de presentación. El orden es el del enum, no alfabético: matrículas y
    /// traspasos son el grueso de la operación y «otros» es el cajón, así que va al final.
    /// </summary>
    private static readonly (string Code, string Label)[] Familias =
    [
        (ProcedureFamilyCodes.Matriculas, "Matrículas"),
        (ProcedureFamilyCodes.Traspaso, "Traspasos"),
        (ProcedureFamilyCodes.Otros, "Otros trámites"),
    ];

    /// <summary>
    /// Construye las opciones a partir de los tipos que se pasen.
    /// </summary>
    /// <param name="tipos">
    /// Los tipos presentes en los trámites que quien consulta puede ver, no el catálogo completo.
    /// Ofrecer los veintiún tipos daría dieciocho opciones que devuelven cero, y para una empresa
    /// además revelaría qué tramitan las demás.
    /// </param>
    public static IReadOnlyList<QueryFieldOptionDto> Build(
        IEnumerable<(Guid Id, string Name, string? Family)> tipos)
    {
        var porFamilia = tipos
            .GroupBy(t => ProcedureFamilyCodes.FromCode(t.Family) is ProcedureFamily f
                ? ProcedureFamilyCodes.ToCode(f)
                // Una familia que no pertenece al dominio se recoge en «otros» en vez de perderse:
                // el tipo existe y hay trámites suyos, así que tiene que poder filtrarse.
                : ProcedureFamilyCodes.Otros)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var opciones = new List<QueryFieldOptionDto>();
        foreach (var (code, label) in Familias)
        {
            if (!porFamilia.TryGetValue(code, out var delGrupo) || delGrupo.Count == 0)
            {
                continue;
            }

            opciones.Add(new QueryFieldOptionDto(code, $"Toda la familia: {label}", label));
            opciones.AddRange(delGrupo
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .Select(t => new QueryFieldOptionDto(t.Id.ToString(), t.Name, label)));
        }

        return opciones;
    }
}
