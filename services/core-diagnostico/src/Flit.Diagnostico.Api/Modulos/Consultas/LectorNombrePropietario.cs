using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Modulos.Consultas.Mapeos;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Nombre, en la respuesta cifrada del RUNT, del propietario dueño del documento que va en la solicitud. La lista cruda no sirve por
/// posición: <c>personas_vehiculo</c> sale filtrada y sin repetidos (<see cref="MapperIntempo"/>, <see cref="DocumentosVehiculo"/>),
/// así que el primero crudo puede ser otra persona. Se empareja por documento con la misma regla del mapeo. Sin coincidencia, sin
/// nombre o con nombres distintos para ese documento devuelve null: nunca el nombre de otra persona. No se persiste. Acepta los
/// nombres del proto y el camelCase.
/// </summary>
internal static class LectorNombrePropietario
{
    /// <param name="json">Respuesta del RUNT descifrada, en memoria.</param>
    /// <param name="documento">Documento ya normalizado (<see cref="EjecutorCeldaSimitPersona.Normalizar"/>) que va en la solicitud.</param>
    public static string? Leer(string? json, DocumentoPersona documento)
    {
        ArgumentNullException.ThrowIfNull(documento);
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var respuesta = JsonDocument.Parse(json);
            if (!Propiedad(respuesta.RootElement, "vehiculo", "vehiculo", out var vehiculo))
                return null;
            if (!Propiedad(vehiculo, "propietarios_actuales", "propietariosActuales", out var lista) || lista.ValueKind != JsonValueKind.Array)
                return null;

            string? hallado = null;
            foreach (var propietario in lista.EnumerateArray())
            {
                if (!EsDe(propietario, documento) || Texto(propietario, "nombre_completo", "nombreCompleto")?.Trim() is not { Length: > 0 } nombre)
                    continue;
                if (hallado is not null && !string.Equals(hallado, nombre, StringComparison.Ordinal))
                    return null;
                hallado = nombre;
            }

            return hallado;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool EsDe(JsonElement propietario, DocumentoPersona documento)
    {
        var suyo = MapperIntempo.DocumentoPropietario(
            Texto(propietario, "id_tipo_documento", "idTipoDocumento"),
            Texto(propietario, "tipo_documento", "tipoDocumento"),
            Texto(propietario, "no_documento", "noDocumento"));
        return suyo is { } d && EjecutorCeldaSimitPersona.Normalizar(new DocumentoPersona(d.Tipo, d.Numero)) == documento;
    }

    private static string? Texto(JsonElement elemento, string snake, string camel) =>
        Propiedad(elemento, snake, camel, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() : null;

    private static bool Propiedad(JsonElement elemento, string snake, string camel, out JsonElement valor)
    {
        if (elemento.ValueKind == JsonValueKind.Object && (elemento.TryGetProperty(snake, out valor) || elemento.TryGetProperty(camel, out valor)))
            return true;
        valor = default;
        return false;
    }
}
