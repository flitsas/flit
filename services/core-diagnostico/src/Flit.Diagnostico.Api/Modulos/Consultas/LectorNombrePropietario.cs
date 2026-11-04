using System.Text.Json;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Nombre del primer propietario en la respuesta cifrada del RUNT. Es el de orden 1, el mismo documento que va en la solicitud.
/// No se persiste. Acepta los nombres del proto y el camelCase.
/// </summary>
internal static class LectorNombrePropietario
{
    public static string? Leer(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var documento = JsonDocument.Parse(json);
            if (!Propiedad(documento.RootElement, "vehiculo", out var vehiculo))
                return null;
            if (!Propiedad(vehiculo, "propietarios_actuales", "propietariosActuales", out var lista) || lista.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var propietario in lista.EnumerateArray())
            {
                if (Propiedad(propietario, "nombre_completo", "nombreCompleto", out var nombre) && nombre.ValueKind == JsonValueKind.String)
                {
                    var texto = nombre.GetString()?.Trim();
                    return string.IsNullOrEmpty(texto) ? null : texto;
                }

                return null;
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool Propiedad(JsonElement elemento, string nombre, out JsonElement valor) =>
        Propiedad(elemento, nombre, nombre, out valor);

    private static bool Propiedad(JsonElement elemento, string snake, string camel, out JsonElement valor)
    {
        if (elemento.ValueKind == JsonValueKind.Object && (elemento.TryGetProperty(snake, out valor) || elemento.TryGetProperty(camel, out valor)))
            return true;
        valor = default;
        return false;
    }
}
