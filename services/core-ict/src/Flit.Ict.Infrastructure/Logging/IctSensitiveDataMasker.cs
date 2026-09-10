using System.Text;
using System.Text.Json;

namespace Flit.Ict.Infrastructure.Logging;

/// <summary>
/// Redacción/enmascarado de datos sensibles para los logs (doble barrera). Barrera 1: al capturar,
/// las cabeceras sensibles (authorization/cookie/tokens) se reemplazan por ***REDACTED***. Barrera 2:
/// al servir, los valores PII conocidos se enmascaran dejando los últimos 4 caracteres.
/// </summary>
public static class IctSensitiveDataMasker
{
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization", "cookie", "set-cookie", "x-api-key", "x-api-token", "token",
        "password", "secret", "client-secret",
    };

    private static readonly string[] PiiKeyFragments =
    [
        "document", "cedula", "nit", "correo", "email", "telefono", "phone",
        "nombre", "name", "direccion", "address",
    ];

    public const string Redacted = "***REDACTED***";

    /// <summary>Redacta cabeceras sensibles y devuelve un JSON listo para persistir.</summary>
    public static string RedactHeaders(IEnumerable<KeyValuePair<string, string>> headers)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in headers)
        {
            map[key] = SensitiveKeys.Contains(key) ? Redacted : value;
        }

        return JsonSerializer.Serialize(map);
    }

    /// <summary>Enmascara valores PII de un JSON de objeto plano (barrera 2, al servir).</summary>
    public static string? MaskJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return json;
            }

            var masked = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var value = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.ToString();
                masked[prop.Name] = IsSensitive(prop.Name) ? Redacted
                    : IsPii(prop.Name) ? MaskValue(value)
                    : value;
            }

            return JsonSerializer.Serialize(masked);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    /// <summary>
    /// Enmascara el CUERPO (request/response) de un log ICT de forma RECURSIVA: recorre el árbol JSON
    /// completo y, en cualquier nivel de anidamiento, redacta las claves sensibles (tokens/secretos) y
    /// enmascara las claves PII (documento/nombre/correo/…) dejando los últimos 4. Es la barrera 1 (al
    /// capturar): a diferencia de <see cref="MaskJson"/> (plano, para servir) NUNCA devuelve el crudo — si
    /// el texto no es JSON válido (o llegó truncado) devuelve un placeholder, para no persistir PII/secretos
    /// sin enmascarar. Se aplica al request del /register (que trae seller/buyer/lessee con documentos).
    /// </summary>
    public static string? MaskJsonBody(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                WriteMaskedElement(writer, doc.RootElement);
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return "<cuerpo no capturable (no-JSON o truncado)>";
        }
    }

    private static void WriteMaskedElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in element.EnumerateObject())
                {
                    writer.WritePropertyName(prop.Name);
                    if (IsSensitive(prop.Name))
                    {
                        writer.WriteStringValue(Redacted);
                    }
                    else if (IsPii(prop.Name) && IsScalar(prop.Value))
                    {
                        writer.WriteStringValue(MaskValue(ScalarToString(prop.Value)));
                    }
                    else
                    {
                        WriteMaskedElement(writer, prop.Value);
                    }
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteMaskedElement(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static bool IsScalar(JsonElement el) =>
        el.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False;

    private static string ScalarToString(JsonElement el) =>
        el.ValueKind == JsonValueKind.String ? el.GetString() ?? string.Empty : el.GetRawText();

    private static bool IsSensitive(string key) => SensitiveKeys.Contains(key);

    private static bool IsPii(string key) =>
        PiiKeyFragments.Any(f => key.Contains(f, StringComparison.OrdinalIgnoreCase));

    private static string? MaskValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= 4 ? new string('*', value.Length) : new string('*', value.Length - 4) + value[^4..];
    }
}
