using System.Text.Json;
using Flit.DrFlit.Application.Manual;

namespace Flit.DrFlit.Application.Chat;

/// <summary>Respuesta del modelo ya validada contra el contrato y contra el catálogo del manual.</summary>
/// <param name="Citations">
/// Artículos citados que SÍ existen en el catálogo, sin duplicados y en el orden en que los citó el
/// modelo. Solo se rellena para <see cref="DrFlitIntent.Duda"/>.
/// </param>
public sealed record DrFlitModelReply(
    DrFlitIntent Intent,
    string Reply,
    IReadOnlyList<DrFlitManualArticle> Citations);

/// <summary>
/// Valida server-side la salida cruda del modelo (HU #12918, ADR-0060 §8.2.5). No confía en nada de lo
/// que llega: si la salida no cumple el contrato, devuelve <c>null</c> y quien llama lo trata como fallo
/// del LLM (degradación, §7.1). Nunca devuelve una respuesta a medio validar.
/// </summary>
public static class DrFlitModelReplyParser
{
    /// <summary>
    /// Interpreta <paramref name="raw"/> como el objeto <c>{intent, reply, citedSlugs}</c>.
    /// </summary>
    /// <returns>
    /// La respuesta validada, o <c>null</c> si no es JSON, si falta <c>intent</c>/<c>reply</c>, si
    /// <c>intent</c> está fuera del enum, si <c>citedSlugs</c> no es un arreglo de strings, o si una
    /// "duda" cita slugs y ninguno existe en el catálogo.
    /// </returns>
    public static DrFlitModelReply? Parse(
        string? raw,
        IReadOnlyDictionary<string, DrFlitManualArticle> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var json = ExtractObject(raw);
        if (json is null)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            if (!root.TryGetProperty("intent", out var intentProp)
                || intentProp.ValueKind != JsonValueKind.String
                || !DrFlitIntentWire.TryParse(intentProp.GetString(), out var intent))
            {
                return null;
            }

            if (!root.TryGetProperty("reply", out var replyProp)
                || replyProp.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var reply = replyProp.GetString()!.Trim();
            if (reply.Length == 0)
                return null;

            var cited = ReadCitedSlugs(root);
            if (cited is null)
                return null;

            if (intent != DrFlitIntent.Duda)
                return new DrFlitModelReply(intent, reply, []);

            // AC3 — un slug que no existe se descarta en silencio; pero si el modelo citó y NINGUNO
            // existe, la respuesta entera se apoya en algo inventado y no se muestra.
            var citations = new List<DrFlitManualArticle>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var slug in cited)
            {
                if (seen.Add(slug) && catalog.TryGetValue(slug, out var article))
                    citations.Add(article);
            }

            if (cited.Count > 0 && citations.Count == 0)
                return null;

            return new DrFlitModelReply(intent, reply, citations);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// <c>citedSlugs</c> es opcional (ausente o null ⇒ vacío). Si viene, tiene que ser un arreglo de
    /// strings: cualquier otra forma es una desviación del contrato y devuelve <c>null</c>.
    /// </summary>
    private static List<string>? ReadCitedSlugs(JsonElement root)
    {
        if (!root.TryGetProperty("citedSlugs", out var prop) || prop.ValueKind == JsonValueKind.Null)
            return [];

        if (prop.ValueKind != JsonValueKind.Array)
            return null;

        var slugs = new List<string>();
        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                return null;

            var slug = item.GetString()!.Trim();
            if (slug.Length > 0)
                slugs.Add(slug);
        }

        return slugs;
    }

    /// <summary>
    /// Recorta del primer <c>{</c> al último <c>}</c>. Pese a la instrucción, el modelo a veces envuelve
    /// el objeto en un bloque <c>```json</c> o le añade un párrafo (medido en producción con el
    /// clasificador OCR, ver <c>AnthropicDocumentBatchClassifierTests</c>). Lo de afuera se descarta
    /// sin mostrarse: solo sale al usuario el <c>reply</c> ya validado.
    /// </summary>
    private static string? ExtractObject(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var start = raw.IndexOf('{', StringComparison.Ordinal);
        var end = raw.LastIndexOf('}');
        return start >= 0 && end > start ? raw[start..(end + 1)] : null;
    }
}
