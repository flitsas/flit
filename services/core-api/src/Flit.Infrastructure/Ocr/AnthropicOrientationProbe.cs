using System.Text.Json.Nodes;
using Flit.Tramites.Application.Ocr;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Ocr;

/// <summary>
/// HU #12036 — sonda de orientación contra el modelo de visión. Es deliberadamente pequeña: mira UNA
/// página, no pide leer nada y responde un booleano, así que cuesta ~3.300 tokens de entrada y 
/// unas decenas de salida. Ese precio es lo que compra no enviar a analizar un documento que el modelo
/// no puede leer y sobre el que respondería inventándose los datos.
/// </summary>
internal sealed class AnthropicOrientationProbe(
    AnthropicMessagesClient client,
    ILogger<AnthropicOrientationProbe> logger) : IDocumentOrientationProbe
{
    /// <summary>La respuesta es <c>{"derecha":true}</c>: no hace falta más techo que este.</summary>
    private const int MaxTokens = 100;

    /// <summary>La sonda va en el camino crítico de cada carga: si tarda, no vale la pena esperarla.</summary>
    private const int TimeoutSeconds = 30;

    public async Task<PageOrientation> ProbeAsync(ReadOnlyMemory<byte> pdf, CancellationToken ct)
    {
        var base64 = Convert.ToBase64String(pdf.Span);
        var vision = await client.SendVisionAsync(
            base64, "application/pdf", DocumentOcrPrompts.OrientationProbePrompt, ct,
            maxTokens: MaxTokens, timeoutSeconds: TimeoutSeconds);

        if (!vision.Ok)
            return PageOrientation.Unknown;

        if (OcrModelJson.ExtractObject(vision.Text) is not { } obj)
        {
            OrientationProbeLog.ParseFailed(logger);
            return PageOrientation.Unknown;
        }

        // Solo un false explícito significa «girada». Un campo ausente o de otro tipo es Unknown, y
        // Unknown deja el documento como está: ante la duda no se toca lo que el usuario subió.
        return obj["derecha"] switch
        {
            JsonValue v when v.TryGetValue<bool>(out var derecha) => derecha ? PageOrientation.Upright : PageOrientation.Rotated,
            _ => PageOrientation.Unknown,
        };
    }
}

/// <summary>Logging source-generated (CA1848). No loguea el contenido del documento.</summary>
internal static partial class OrientationProbeLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Sonda de orientación: no se pudo parsear la respuesta del modelo")]
    public static partial void ParseFailed(ILogger logger);
}
