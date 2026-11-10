using Flit.Comparendos.Domain.Textos;

namespace Flit.Comparendos.Api.Modulos.Catalogos;

/// <summary>
/// GDC punto 3: la plataforma trae organismos repetidos que solo difieren en espacios (Restrepo/Meta 50606000 y
/// 50606001 «INST DPTAL TTOyTTE  META/RESTREPO», Arauca 81001000 y 81001001). Son gemelos: mismo municipio (5 primeras
/// cifras del código) y mismo nombre una vez normalizado (<see cref="TextoNormalizado"/> junta los espacios). El
/// titular es el de código menor; los demás no se crean, y para mapear cuentan como el titular.
/// </summary>
public static class OrganismosGemelos
{
    /// <summary>Código de 8 cifras (los de 7 perdieron el cero inicial), o null si el código no es numérico.</summary>
    public static string? Codigo8(string codigo) =>
        codigo.Length is > 0 and <= 8 && codigo.All(char.IsAsciiDigit) ? codigo.PadLeft(8, '0') : null;

    /// <summary>Llave del gemelo: municipio + nombre normalizado. null si el código no es numérico.</summary>
    public static string? Llave(string codigo, string nombre) =>
        Codigo8(codigo) is { } c ? $"{c[..5]}|{TextoNormalizado.De(nombre)}" : null;

    /// <summary>Para cada organismo, el id de su titular (él mismo si no tiene gemelos).</summary>
    public static Dictionary<Guid, Guid> Titulares<T>(IEnumerable<T> organismos, Func<T, Guid> id, Func<T, string> codigo, Func<T, string> nombre)
    {
        var lista = organismos.ToList();
        var titulares = lista.ToDictionary(id, id);
        foreach (var grupo in lista.Where(o => Llave(codigo(o), nombre(o)) is not null)
                     .GroupBy(o => Llave(codigo(o), nombre(o))!, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            var titular = id(grupo.OrderBy(o => Codigo8(codigo(o)), StringComparer.Ordinal).First());
            foreach (var o in grupo)
                titulares[id(o)] = titular;
        }

        return titulares;
    }
}
