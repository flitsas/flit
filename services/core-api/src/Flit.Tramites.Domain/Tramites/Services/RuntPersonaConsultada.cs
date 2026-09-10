using System.Text.Json;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// Evidencia de que el documento de una persona se consultó realmente en el RUNT dentro de un
/// trámite. Se registra como evento de la instancia al resolverse la consulta con resultado
/// encontrado.
///
/// <para>Existe porque el wizard daba la consulta por hecha con solo tener el documento digitado:
/// bastaba escribir una cédula para que el gate del actor pasara. Con esta evidencia el gate puede
/// exigir la consulta de verdad en los actores cuyo tipo de trámite la marca con
/// <c>requiresRunt</c>.</para>
/// </summary>
public static class RuntPersonaConsultada
{
    public const string Tipo = "runt_persona_consultada";

    /// <summary>Clave de comparación: tipo y número normalizados, sin espacios ni mayúsculas.</summary>
    public static string Key(string? documentType, string? documentNumber) =>
        $"{documentType?.Trim().ToUpperInvariant()}|{documentNumber?.Trim().ToUpperInvariant()}";

    public static string Payload(string documentType, string documentNumber) =>
        JsonSerializer.Serialize(new
        {
            documentType = documentType.Trim().ToUpperInvariant(),
            documentNumber = documentNumber.Trim(),
        });

    /// <summary>
    /// Lee la clave del payload de un evento. Devuelve <c>null</c> si el payload no tiene la forma
    /// esperada (evento de otra versión o corrupto): un dato ilegible no cuenta como evidencia.
    /// </summary>
    public static string? KeyFromPayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            var type = doc.RootElement.TryGetProperty("documentType", out var t) ? t.GetString() : null;
            var number = doc.RootElement.TryGetProperty("documentNumber", out var n) ? n.GetString() : null;
            return string.IsNullOrWhiteSpace(number) ? null : Key(type, number);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
