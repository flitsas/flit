using System.Text.Json;
using System.Text.Json.Nodes;
using Flit.Tramites.Application.Ocr;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Ocr;

/// <summary>
/// Analizador OCR real: envía el documento al modelo de visión de Anthropic con el prompt del tipo
/// (<see cref="DocumentOcrPrompts"/>) y parsea el JSON devuelto. Implementa
/// <see cref="IDocumentOcrAnalyzer"/> y se activa con <c>Ocr:Provider = anthropic</c> (si no, corre el
/// mock). La resiliencia (timeout/reintento/degradación) vive en <see cref="AnthropicMessagesClient"/>.
/// Flujo: base64 → visión → parseo JSON (quitando fences ```json).
/// </summary>
internal sealed class AnthropicDocumentOcrAnalyzer(
    AnthropicMessagesClient client,
    ILogger<AnthropicDocumentOcrAnalyzer> logger) : IDocumentOcrAnalyzer
{
    public async Task<DocumentOcrAnalysis> AnalyzeAsync(
        string tipo, ReadOnlyMemory<byte> content, string mediaType, CancellationToken ct)
    {
        var prompt = DocumentOcrPrompts.PromptFor(tipo);
        if (prompt is null)
            return new DocumentOcrAnalysis(false, null, 400, $"Tipo no soportado: {tipo}");

        var base64 = Convert.ToBase64String(content.Span);
        var vision = await client.SendVisionAsync(base64, mediaType, prompt, ct);
        if (!vision.Ok)
            return new DocumentOcrAnalysis(false, null, vision.Status, vision.Message);

        var json = StripCodeFences(vision.Text!);
        try
        {
            if (JsonNode.Parse(json) is JsonObject obj)
                return new DocumentOcrAnalysis(true, obj, 200, null);
        }
        catch (JsonException)
        {
            // Cae al retorno de error de abajo.
        }

        OcrParseLog.ParseFailed(logger, tipo);
        return new DocumentOcrAnalysis(false, null, 500, "No se pudo extraer datos del documento");
    }

    /// <summary>Quita fences de markdown (```json … ```) que el modelo pueda envolver alrededor del JSON.</summary>
    private static string StripCodeFences(string text)
    {
        var t = text.Trim();
        if (t.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            t = t["```json".Length..];
        else if (t.StartsWith("```", StringComparison.Ordinal))
            t = t["```".Length..];
        if (t.EndsWith("```", StringComparison.Ordinal))
            t = t[..^3];
        return t.Trim();
    }
}

/// <summary>Logging source-generated (CA1848) del parseo OCR. No loguea el contenido del documento.</summary>
internal static partial class OcrParseLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Anthropic OCR: no se pudo parsear el JSON del modelo (tipo {Tipo})")]
    public static partial void ParseFailed(ILogger logger, string tipo);
}
