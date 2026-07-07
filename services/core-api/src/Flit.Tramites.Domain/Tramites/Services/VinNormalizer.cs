using System.Text;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// Normaliza un VIN para comparación determinista de la invariante "un VIN → una matrícula"
/// (HU #10538, R3). Un VIN es estrictamente alfanumérico, así que se descarta cualquier ruido de
/// formato (espacios, guiones, puntos) y se lleva a mayúsculas: <c>"8ab 12-3"</c> y <c>"8AB123"</c>
/// comparan igual. NO elimina letras (no aplica la exclusión ISO de I/O/Q: eso corrompería VINs
/// legítimos de proveedores que no la respetan); solo elimina lo que no es letra ni dígito.
/// Devuelve <c>null</c> si la entrada es vacía o queda vacía tras normalizar.
/// </summary>
public static class VinNormalizer
{
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToUpperInvariant(ch));
        }

        return sb.Length == 0 ? null : sb.ToString();
    }
}
