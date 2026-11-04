using System.Text.Json;
using Flit.PersonaNatural.Api.Definiciones;

namespace Flit.PersonaNatural.Api.Punto;

/// <summary>
/// Un rol de la definición del tipo tal como lo pinta el punto. <see cref="EsRadicante"/> marca el PRIMER rol de la definición
/// (ordinal 1): quien radica en el punto. Los demás son las partes invitables. Ningún rol ni tipo se nombra en el BFF.
/// </summary>
public sealed record RolPuntoDto(string Rol, string Etiqueta, bool EsRadicante, int Minimo, int Maximo, bool PersonaNaturalPermitida);

/// <summary>Deriva los roles del canal del punto de la definición del tipo (paridad con el portal web).</summary>
public static class RolesPunto
{
    public static IReadOnlyList<RolPuntoDto> Desde(DefinicionTipoDto def)
    {
        ArgumentNullException.ThrowIfNull(def);
        var salida = new List<RolPuntoDto>();
        foreach (var a in def.Actores)
        {
            if (string.IsNullOrWhiteSpace(a.Rol))
                continue;
            salida.Add(new RolPuntoDto(a.Rol.Trim(), a.Etiqueta, salida.Count == 0, a.Min, a.Max, a.PersonaNaturalPermitida));
        }

        return salida;
    }

    /// <summary>
    /// Lo mismo desde el snapshot congelado de la solicitud. Tolerante con la forma (<c>actores[]</c> con <c>rol</c>,
    /// <c>etiqueta</c>, <c>min</c>, <c>max</c>; mayúsculas y guiones bajos no cuentan) y conserva el orden de la definición.
    /// </summary>
    public static IReadOnlyList<RolPuntoDto> Desde(string? snapshot)
    {
        var salida = new List<RolPuntoDto>();
        if (string.IsNullOrWhiteSpace(snapshot))
            return salida;
        try
        {
            using var doc = JsonDocument.Parse(snapshot);
            if (Propiedad(doc.RootElement, "actores") is not { ValueKind: JsonValueKind.Array } actores)
                return salida;
            foreach (var actor in actores.EnumerateArray())
            {
                if (Propiedad(actor, "rol") is not { ValueKind: JsonValueKind.String } rolJson || string.IsNullOrWhiteSpace(rolJson.GetString()))
                    continue;
                var rol = rolJson.GetString()!.Trim();
                var etiqueta = Propiedad(actor, "etiqueta") is { ValueKind: JsonValueKind.String } e && !string.IsNullOrWhiteSpace(e.GetString()) ? e.GetString()! : rol;
                var min = Propiedad(actor, "min") is { ValueKind: JsonValueKind.Number } n && n.TryGetInt32(out var vmin) ? vmin : 1;
                var max = Propiedad(actor, "max") is { ValueKind: JsonValueKind.Number } x && x.TryGetInt32(out var vmax) ? vmax : 1;
                var permitida = Propiedad(actor, "personaNaturalPermitida") is not { ValueKind: JsonValueKind.False };
                salida.Add(new RolPuntoDto(rol, etiqueta, salida.Count == 0, min, max, permitida));
            }
        }
        catch (JsonException)
        {
            // Un snapshot ilegible no inventa roles.
        }

        return salida;
    }

    private static JsonElement? Propiedad(JsonElement elemento, string nombre)
    {
        if (elemento.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var propiedad in elemento.EnumerateObject())
        {
            if (string.Equals(propiedad.Name.Replace("_", string.Empty, StringComparison.Ordinal), nombre, StringComparison.OrdinalIgnoreCase))
                return propiedad.Value;
        }

        return null;
    }
}
