using System.Text.Json;
using System.Text.Json.Nodes;

namespace Flit.Tramites.Application.UseCases.Certifications;

/// <summary>
/// Limpia la respuesta cruda de un proveedor antes de guardarla (HU #11304, ADR-0041).
/// </summary>
/// <remarks>
/// El payload se conserva de forma indefinida (D6) y el del RUES incluye <b>PII</b>: nombres y
/// documentos de representantes legales, y un texto de facultades que puede llevar más nombres
/// embebidos. La retención la decidió el PO; lo que no se negocia es que no se guarde lo que no hace
/// falta para reprocesar.
///
/// <para>Qué se retira, y por qué solo esto:</para>
/// <list type="bullet">
///   <item><b>Credenciales y tokens.</b> Un <c>authorization</c> o un <c>apiKey</c> que venga reflejado
///         en la respuesta no aporta nada al reproceso y convierte la tabla en un almacén de secretos.</item>
///   <item><b>Campos de imagen o documento en base64.</b> Multiplican el tamaño por miles sin aportar
///         un solo dato certificable.</item>
/// </list>
///
/// <para>Lo demás se conserva <b>tal cual</b>. Recortar más volvería a producir el problema que este
/// Feature corrige: un almacén que decide por adelantado qué es importante, y que por tanto no sirve
/// para descubrir que el modelo se equivocaba.</para>
///
/// <para><b>Prohibido</b> volcar el resultado en trazas, logs, PRs o comentarios de ADO (Ley 1581).</para>
/// </remarks>
public static class RawPayloadSanitizer
{
    /// <summary>Marcador que sustituye a un valor retirado, para que se note que existía.</summary>
    public const string Redacted = "[redactado]";

    /// <summary>Tope de tamaño. Por encima se descarta: un payload así no es una respuesta, es un adjunto.</summary>
    public const int MaxLength = 512 * 1024;

    private static readonly HashSet<string> SecretNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization", "auth", "token", "accessToken", "access_token", "refreshToken",
        "refresh_token", "apiKey", "api_key", "clientSecret", "client_secret", "password",
        "secret", "bearer", "xApiKey", "x-api-key",
    };

    private static readonly HashSet<string> BinaryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "base64", "imagen", "image", "foto", "photo", "pdf", "archivo", "file", "contenido",
        "content", "documentoBase64", "imagenBase64", "fileContent",
    };

    /// <summary>Longitud a partir de la cual una cadena en un campo binario se considera un adjunto.</summary>
    private const int BinaryValueThreshold = 2_000;

    /// <summary>
    /// Devuelve el JSON saneado, o <c>null</c> si no hay nada utilizable (vacío, ilegible o
    /// desmesurado). Nunca lanza: un payload raro no puede tumbar una consulta ya respondida.
    /// </summary>
    public static string? Sanitize(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson) || rawJson.Length > MaxLength)
            return null;

        try
        {
            var node = JsonNode.Parse(rawJson);
            if (node is null)
                return null;

            Scrub(node);
            return node.ToJsonString();
        }
        catch (JsonException)
        {
            // Si no es JSON válido no se guarda: el almacén es para reprocesar, y no se puede
            // reprocesar lo que no se puede volver a deserializar.
            return null;
        }
    }

    private static void Scrub(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj.ToList())
                {
                    if (SecretNames.Contains(name))
                    {
                        obj[name] = Redacted;
                        continue;
                    }

                    if (BinaryNames.Contains(name)
                        && child is JsonValue value
                        && value.TryGetValue<string>(out var text)
                        && text.Length > BinaryValueThreshold)
                    {
                        obj[name] = Redacted;
                        continue;
                    }

                    if (child is not null)
                        Scrub(child);
                }

                break;

            case JsonArray array:
                foreach (var item in array)
                {
                    if (item is not null)
                        Scrub(item);
                }

                break;
        }
    }
}
