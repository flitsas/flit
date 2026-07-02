using System.Text.Json.Nodes;

namespace Flit.Tramites.Application.Ocr;

/// <summary>Respuesta OK del endpoint OCR: <c>{ ok, tipo, data }</c>.</summary>
/// <param name="Ok">Siempre true en éxito.</param>
/// <param name="Tipo">Tipo analizado.</param>
/// <param name="Data">JSON extraído por el analizador.</param>
public sealed record DocumentOcrResponse(bool Ok, string Tipo, JsonObject? Data);

/// <summary>
/// Fallo del análisis con código HTTP + mensaje legible. El endpoint lo mapea a <c>Results.Problem</c>.
/// Unifica errores de validación (400) y de degradación del proveedor (503/500) en un único tipo.
/// </summary>
public sealed record OcrFailure(int Status, string Message);

/// <summary>
/// Handler del análisis OCR de un documento de trámite. Valida tipo soportado, presencia del archivo,
/// tamaño máximo (10 MB) y formato por magic bytes (PDF/JPG/PNG); luego delega en
/// <see cref="IDocumentOcrAnalyzer"/> (mock o Anthropic según config). NO persiste nada: el OCR es
/// stateless y ocurre ANTES del flujo S3 (presign/register) del expediente.
/// </summary>
public sealed class AnalyzeDocumentHandler(IDocumentOcrAnalyzer analyzer)
{
    /// <summary>Tamaño máximo del archivo a analizar (10 MB).</summary>
    public const long MaxFileBytes = 10L * 1024 * 1024;

    public async Task<(DocumentOcrResponse? Result, OcrFailure? Failure)> HandleAsync(
        string tipo, ReadOnlyMemory<byte> content, CancellationToken ct)
    {
        if (!DocumentOcrPrompts.IsSupported(tipo))
            return (null, new OcrFailure(400, $"Tipo no soportado: {tipo}"));
        if (content.Length == 0)
            return (null, new OcrFailure(400, "Archivo requerido"));
        if (content.Length > MaxFileBytes)
            return (null, new OcrFailure(400, "Archivo máximo 10MB"));
        if (!TryResolveMediaType(content.Span, out var mediaType))
            return (null, new OcrFailure(400, "Solo PDF, JPG o PNG"));

        var analysis = await analyzer.AnalyzeAsync(tipo, content, mediaType, ct);
        if (!analysis.Ok)
            return (null, new OcrFailure(analysis.Status, analysis.Message ?? "No se pudo analizar el documento"));

        return (new DocumentOcrResponse(true, tipo, analysis.Data), null);
    }

    /// <summary>
    /// Resuelve el MIME por magic bytes (no por el MIME declarado por el cliente):
    /// PDF (<c>%PDF</c>), JPG (<c>FF D8</c>), PNG (<c>89 50</c>). Devuelve false si no es ninguno.
    /// </summary>
    private static bool TryResolveMediaType(ReadOnlySpan<byte> bytes, out string mediaType)
    {
        if (bytes.Length >= 4 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46)
        {
            mediaType = "application/pdf";
            return true;
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            mediaType = "image/jpeg";
            return true;
        }
        if (bytes.Length >= 2 && bytes[0] == 0x89 && bytes[1] == 0x50)
        {
            mediaType = "image/png";
            return true;
        }
        mediaType = string.Empty;
        return false;
    }
}
