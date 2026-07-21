using System.Text.Json;
using System.Text.Json.Nodes;

namespace Flit.Tramites.Application.UseCases.ProcedureTypes;

/// <summary>
/// Utilidades de (de)serialización tolerante de los perfiles JSONB del configurador (CFD-01):
/// <c>gate_profile</c> del tipo y <c>validation_profile</c> de las reglas de conformación. Un JSON
/// vacío/nulo/corrupto degrada a objeto vacío en lugar de propagar excepción — el perfil es
/// configuración, no un invariante del dominio.
/// </summary>
internal static class ProfileJson
{
    /// <summary>Parsea un JSON almacenado a <see cref="JsonNode"/>; objeto vacío si es nulo/vacío/inválido.</summary>
    public static JsonNode ParseOrEmpty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new JsonObject();

        try
        {
            return JsonNode.Parse(json) ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }
}
