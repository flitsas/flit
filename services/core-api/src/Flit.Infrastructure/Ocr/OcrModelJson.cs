using System.Text.Json;
using System.Text.Json.Nodes;

namespace Flit.Infrastructure.Ocr;

/// <summary>
/// Rescata el objeto JSON de una respuesta del modelo de visión.
///
/// <para>Los prompts piden «JSON valido sin markdown» y la mayoría de las veces eso es exactamente lo
/// que llega. Pero cuando el documento no encaja del todo en el esquema pedido —una declaración de
/// importación que ampara 40 vehículos contra un esquema de uno solo— el modelo tiende a cerrar el
/// JSON y añadir un párrafo explicando por qué. Exigir que la respuesta sea <em>exactamente</em> un
/// documento JSON tiraba esa extracción entera: medido sobre 22 expedientes reales, el 27 % de los
/// documentos de aduana se perdía así, con el operador viendo «No se pudo extraer datos».</para>
///
/// <para>Por eso se busca el primer objeto <c>{…}</c> equilibrado en vez de parsear el texto completo:
/// la prosa de alrededor —antes, después, o en fences de markdown— deja de costar el dato. Lo que NO
/// se rescata es un JSON truncado a media llave (sin cierre no hay objeto que extraer), y eso está
/// bien: ahí el dato de verdad está incompleto y degradar es lo correcto.</para>
/// </summary>
internal static class OcrModelJson
{
    /// <summary>
    /// Devuelve el primer objeto JSON de <paramref name="text"/>, o null si no hay ninguno completo.
    /// </summary>
    public static JsonObject? ExtractObject(string? text)
    {
        var slice = FirstBalancedObject(text);
        if (slice is null)
            return null;

        try
        {
            return JsonNode.Parse(slice) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Recorta el primer <c>{…}</c> equilibrado, respetando llaves dentro de cadenas y escapes. Sin
    /// esto, un valor como <c>"observaciones": "cerrado con }"</c> cortaría el objeto a mitad.
    /// </summary>
    internal static string? FirstBalancedObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var start = text.IndexOf('{', StringComparison.Ordinal);
        if (start < 0)
            return null;

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (inString)
            {
                if (c == '\\')
                    escaped = true;
                else if (c == '"')
                    inString = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                        return text[start..(i + 1)];
                    break;
            }
        }

        // Llegó el final sin cerrar: la respuesta viene truncada (típicamente por max_tokens).
        return null;
    }
}
