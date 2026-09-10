using System.Text.Json.Nodes;
using Flit.Tramites.Application.Ocr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Ocr;

/// <summary>
/// Clasificador real del cargue masivo: manda el archivo completo al modelo de visión con el prompt de
/// clasificación (<see cref="DocumentOcrPrompts.ClassificationPrompt"/>) y parsea el mapa
/// <c>tipo → páginas</c> que devuelve. Corre con el modelo fuerte
/// (<see cref="AnthropicOptions.ClassifierModel"/>) y un deadline propio, porque es la única llamada que
/// ve el expediente entero. La resiliencia (timeout/reintento/degradación) vive en
/// <see cref="AnthropicMessagesClient"/>, igual que en el analizador por tipo.
/// </summary>
internal sealed class AnthropicDocumentBatchClassifier(
    AnthropicMessagesClient client,
    IOptions<AnthropicOptions> options,
    ILogger<AnthropicDocumentBatchClassifier> logger) : IDocumentBatchClassifier
{
    private readonly AnthropicOptions _options = options.Value;

    public async Task<BatchClassification> ClassifyAsync(
        IReadOnlyCollection<string> tiposEsperados,
        ReadOnlyMemory<byte> content,
        string mediaType,
        CancellationToken ct)
    {
        var soportados = tiposEsperados.Where(DocumentOcrPrompts.IsSupported).Distinct(StringComparer.Ordinal).ToList();
        if (soportados.Count == 0)
            return BatchClassification.Failure(400, "No hay tipos de documento que clasificar.");

        var prompt = DocumentOcrPrompts.ClassificationPrompt(soportados);
        var base64 = Convert.ToBase64String(content.Span);

        var vision = await client.SendVisionAsync(
            base64, mediaType, prompt, ct,
            model: _options.ClassifierModel,
            maxTokens: _options.ClassifierMaxTokens,
            timeoutSeconds: _options.ClassifierTimeoutSeconds);

        if (!vision.Ok)
            return BatchClassification.Failure(vision.Status, vision.Message);

        // Mismo criterio tolerante que el analizador por tipo; ver OcrModelJson.
        var root = OcrModelJson.ExtractObject(vision.Text);

        if (root is null)
        {
            BatchClassifierLog.ParseFailed(logger);
            return BatchClassification.Failure(500, "No se pudo interpretar la clasificación del documento.");
        }

        return Parse(root, soportados);
    }

    /// <summary>
    /// Traduce el JSON del modelo al contrato de Application, saneando lo que el modelo puede equivocar:
    /// tipos fuera de la lista pedida, páginas fuera de rango o repetidas entre documentos, y confianzas
    /// fuera de [0,1]. Una página reclamada por dos documentos se le deja al primero — así el operador
    /// nunca ve la misma página propuesta dos veces en la pantalla de revisión.
    /// </summary>
    private static BatchClassification Parse(JsonObject root, IReadOnlyCollection<string> soportados)
    {
        var totalPaginas = ReadInt(root, "total_paginas");
        var permitidos = new HashSet<string>(soportados, StringComparer.Ordinal);
        var asignadas = new HashSet<int>();
        var documentos = new List<ClassifiedDocument>();

        if (root.TryGetPropertyValue("documentos", out var docsNode) && docsNode is JsonArray docs)
        {
            foreach (var el in docs)
            {
                if (el is not JsonObject doc)
                    continue;

                var tipo = ReadString(doc, "tipo");
                if (tipo is null || !permitidos.Contains(tipo))
                    continue;

                var paginas = ReadPages(doc, "paginas", totalPaginas)
                    .Where(asignadas.Add)
                    .ToList();
                if (paginas.Count == 0)
                    continue;

                documentos.Add(new ClassifiedDocument(
                    tipo,
                    paginas,
                    Math.Clamp(ReadDouble(doc, "confianza"), 0d, 1d),
                    ReadString(doc, "motivo")));
            }
        }

        var noReconocidas = ReadPages(root, "paginas_no_reconocidas", totalPaginas)
            .Where(p => !asignadas.Contains(p))
            .ToList();

        return new BatchClassification(true, totalPaginas, documentos, noReconocidas);
    }

    /// <summary>
    /// Lee un array de números de página base 1, descarta los fuera de rango y deduplica conservando el
    /// orden. Con <paramref name="totalPaginas"/> en 0 (el modelo no lo reportó) sólo se exige que sean
    /// positivos: el recorte posterior vuelve a filtrar contra el PDF real.
    /// </summary>
    private static List<int> ReadPages(JsonObject node, string property, int totalPaginas)
    {
        var pages = new List<int>();
        if (!node.TryGetPropertyValue(property, out var value) || value is not JsonArray arr)
            return pages;

        foreach (var el in arr)
        {
            if (el is not JsonValue v || !v.TryGetValue<int>(out var page))
                continue;
            if (page < 1 || (totalPaginas > 0 && page > totalPaginas))
                continue;
            if (!pages.Contains(page))
                pages.Add(page);
        }
        return pages;
    }

    private static string? ReadString(JsonObject node, string property) =>
        node.TryGetPropertyValue(property, out var v) && v is JsonValue jv && jv.TryGetValue<string>(out var s)
            ? s.Trim() is { Length: > 0 } t ? t : null
            : null;

    private static int ReadInt(JsonObject node, string property) =>
        node.TryGetPropertyValue(property, out var v) && v is JsonValue jv && jv.TryGetValue<int>(out var i) ? i : 0;

    private static double ReadDouble(JsonObject node, string property) =>
        node.TryGetPropertyValue(property, out var v) && v is JsonValue jv && jv.TryGetValue<double>(out var d) ? d : 0d;
}

/// <summary>Logging source-generated (CA1848) del clasificador. No loguea el contenido del documento.</summary>
internal static partial class BatchClassifierLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Clasificador de lote: no se pudo parsear el JSON del modelo")]
    public static partial void ParseFailed(ILogger logger);
}
