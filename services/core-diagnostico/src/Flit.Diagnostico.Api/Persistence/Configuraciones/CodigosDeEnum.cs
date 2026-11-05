using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Flit.Diagnostico.Api.Persistence.Configuraciones;

/// <summary>
/// Un enum del dominio se guarda con su código en mayúsculas y guion bajo (<c>RuntPropietario</c> → <c>RUNT_PROPIETARIO</c>),
/// que es lo que validan los CHECK de la base (HU #13736). Una lista de enums se guarda como arreglo jsonb de códigos.
/// </summary>
internal static class CodigosDeEnum
{
    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false) },
    };

    /// <summary>Código en la base de un valor del enum.</summary>
    public static string Codigo<T>(T valor)
        where T : struct, Enum => JsonNamingPolicy.SnakeCaseUpper.ConvertName(valor.ToString());

    /// <summary>Convertidor enum ↔ código para una columna <c>varchar</c>.</summary>
    public static ValueConverter<T, string> Convertidor<T>()
        where T : struct, Enum => new(v => Codigo(v), c => Leer<T>(c));

    /// <summary>Convertidor lista de enums ↔ arreglo jsonb de códigos.</summary>
    public static ValueConverter<List<T>, string> ConvertidorDeLista<T>()
        where T : struct, Enum => new(v => JsonSerializer.Serialize(v, Json), c => JsonSerializer.Deserialize<List<T>>(c, Json) ?? new List<T>());

    /// <summary>Comparador por contenido, para que EF detecte cambios dentro de la lista.</summary>
    public static ValueComparer<List<T>> ComparadorDeLista<T>()
        where T : struct, Enum => new(
            (a, b) => a == null ? b == null : b != null && a.SequenceEqual(b),
            v => v.Aggregate(0, (h, x) => HashCode.Combine(h, x)),
            v => v.ToList());

    /// <summary>Valores permitidos de una columna de código, para su CHECK (<c>'A', 'B'</c>).</summary>
    public static string ListaSql<T>()
        where T : struct, Enum => string.Join(", ", Enum.GetValues<T>().Select(v => $"'{Codigo(v)}'"));

    /// <summary>Arreglo jsonb con todos los códigos del enum, para el CHECK de una columna jsonb (<c>'["A", "B"]'::jsonb</c>).</summary>
    public static string ArregloJsonbSql<T>()
        where T : struct, Enum => $"'{JsonSerializer.Serialize(Enum.GetValues<T>().Select(Codigo).ToList())}'::jsonb";

    /// <summary>Valor del enum cuyo código es <paramref name="codigo"/> (exacto, en mayúsculas); false si no hay ninguno.</summary>
    public static bool TryLeer<T>(string? codigo, out T valor)
        where T : struct, Enum
    {
        foreach (var v in Enum.GetValues<T>())
        {
            if (string.Equals(Codigo(v), codigo, StringComparison.Ordinal))
            {
                valor = v;
                return true;
            }
        }

        valor = default;
        return false;
    }

    private static T Leer<T>(string codigo)
        where T : struct, Enum => Enum.GetValues<T>().First(v => Codigo(v) == codigo);
}
