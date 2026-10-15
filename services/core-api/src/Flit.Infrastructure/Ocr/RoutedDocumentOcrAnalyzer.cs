using Flit.Admin.Domain.Plataforma.Ai;
using Flit.Infrastructure.Ai.Routing;
using Flit.Tramites.Application.Ocr;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Ocr;

/// <summary>
/// OCR vía <see cref="IAiProviderRouter"/> (visión + failover). Mismo parseo JSON que
/// <see cref="AnthropicDocumentOcrAnalyzer"/> (HU #13510).
/// </summary>
internal sealed class RoutedDocumentOcrAnalyzer(
    IAiProviderRouter router,
    ILogger<RoutedDocumentOcrAnalyzer> logger) : IDocumentOcrAnalyzer
{
    public async Task<DocumentOcrAnalysis> AnalyzeAsync(
        string tipo,
        ReadOnlyMemory<byte> content,
        string mediaType,
        CancellationToken ct)
    {
        var prompt = DocumentOcrPrompts.PromptFor(tipo);
        if (prompt is null)
            return new DocumentOcrAnalysis(false, null, 400, $"Tipo no soportado: {tipo}");

        var base64 = Convert.ToBase64String(content.Span);
        var routed = await router
            .InvokeVisionAsync(AiToolCodes.Ocr, base64, mediaType, prompt, ct)
            .ConfigureAwait(false);

        if (!routed.Ok)
        {
            var message = routed.Unavailable
                ? AiRouterResult.UnavailableErrorCode
                : routed.Message;
            return new DocumentOcrAnalysis(false, null, routed.Status, message);
        }

        if (OcrModelJson.ExtractObject(routed.Text) is { } obj)
            return new DocumentOcrAnalysis(true, obj, 200, null);

        RoutedOcrParseLog.ParseFailed(logger, tipo, routed.ProviderCode ?? "?");
        return new DocumentOcrAnalysis(false, null, 500, "No se pudo extraer datos del documento");
    }
}

internal static partial class RoutedOcrParseLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Routed OCR: no se pudo parsear JSON (tipo {Tipo}, provider {Provider})")]
    public static partial void ParseFailed(ILogger logger, string tipo, string provider);
}
