using System.Text.Json;

namespace Flit.Tramites.Domain.Tramites.Quipux;

/// <summary>
/// Detecta si un tipo de trámite está parametrizado para el canal Quipux vía
/// <c>procedure_types.external_refs.quipux</c>. Sin dependencia del módulo Quipux: solo presencia
/// del bloque (misma regla que <c>QuipuxTipoTramiteMap.Parse</c> → null = no elegible).
/// </summary>
public static class ProcedureTypeQuipuxGate
{
    /// <summary>¿El JSON de <c>external_refs</c> declara un objeto <c>quipux</c>?</summary>
    public static bool EstaParametrizadoParaQuipux(string? externalRefs)
    {
        if (string.IsNullOrWhiteSpace(externalRefs))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(externalRefs);
            return doc.RootElement.TryGetProperty("quipux", out var bloque)
                   && bloque.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
