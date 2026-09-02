using System.Text.Json.Nodes;

namespace Flit.Tramites.Application.Ocr;

/// <summary>Respuesta OK del endpoint OCR: <c>{ ok, tipo, data, extractedPdfBase64 }</c>.</summary>
/// <param name="Ok">Siempre true en éxito.</param>
/// <param name="Tipo">Tipo analizado.</param>
/// <param name="Data">JSON extraído por el analizador.</param>
/// <param name="ExtractedPdfBase64">
/// PDF recortado (base64) cuando el documento ocupaba sólo un subconjunto de páginas de un PDF
/// multi-documento; null si no hubo recorte (imagen, PDF de una sola página, o el tipo abarca todo el PDF).
/// El frontend sube este recorte al expediente en vez del PDF completo.
/// </param>
public sealed record DocumentOcrResponse(bool Ok, string Tipo, JsonObject? Data, string? ExtractedPdfBase64 = null);

/// <summary>
/// Fallo del análisis con código HTTP + mensaje legible. El endpoint lo mapea a <c>Results.Problem</c>.
/// Unifica errores de validación (400) y de degradación del proveedor (503/500) en un único tipo.
/// </summary>
public sealed record OcrFailure(int Status, string Message);

/// <summary>
/// Handler del análisis OCR de un documento de trámite. Valida tipo soportado, presencia del archivo,
/// tamaño máximo (10 MB) y formato por magic bytes (PDF/JPG/PNG); luego delega en
/// <see cref="IDocumentOcrAnalyzer"/> (mock o Anthropic según config). Si el análisis indica que el
/// documento ocupa sólo un subconjunto de páginas de un PDF multi-documento, recorta ese subconjunto
/// (<see cref="IPdfPageExtractor"/>) y lo devuelve en base64. NO persiste nada: el OCR es stateless y
/// ocurre ANTES del flujo S3 (presign/register) del expediente.
/// </summary>
public sealed class AnalyzeDocumentHandler(
    IDocumentOcrAnalyzer analyzer,
    IPdfPageExtractor? pdfExtractor = null,
    PdfOrientationNormalizer? orientationNormalizer = null)
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

        // HU #12036 — se endereza ANTES de analizar. Un escaneo girado no se lee mal: se INVENTA, y con
        // `es_valido: true`. Solo se toca lo que se manda al modelo: el recorte de páginas y lo que se
        // sube al expediente siguen saliendo del binario original que cargó el usuario.
        var paraAnalizar = content;
        if (mediaType == "application/pdf" && orientationNormalizer is not null)
            paraAnalizar = await orientationNormalizer.NormalizeAsync(content, ct);

        var analysis = await analyzer.AnalyzeAsync(tipo, paraAnalizar, mediaType, ct);
        if (!analysis.Ok)
            return (null, new OcrFailure(analysis.Status, analysis.Message ?? "No se pudo analizar el documento"));

        var extractedPdfBase64 = TryExtractPdfSubset(mediaType, content, analysis.Data);
        return (new DocumentOcrResponse(true, tipo, analysis.Data, extractedPdfBase64), null);
    }

    /// <summary>
    /// Si el documento es PDF y el análisis reporta un subconjunto de páginas (<c>paginas_documento</c>
    /// con menos páginas que <c>total_paginas</c>), recorta ese subconjunto y devuelve el PDF en base64.
    /// Anota <c>_paginas_extraidas</c>/<c>_paginas_originales</c> en <paramref name="data"/>. Devuelve null
    /// si no aplica o falla el recorte (en cuyo caso se sube el PDF original). Sin extractor registrado → null.
    /// </summary>
    private string? TryExtractPdfSubset(string mediaType, ReadOnlyMemory<byte> content, JsonObject? data)
    {
        if (pdfExtractor is null || mediaType != "application/pdf" || data is null)
            return null;
        if (!TryReadPageSubset(data, out var pages, out var totalPaginas))
            return null;
        // Sólo recortamos cuando el documento ocupa MENOS páginas que el PDF completo.
        if (pages.Count == 0 || pages.Count >= totalPaginas)
            return null;

        var extracted = pdfExtractor.ExtractPages(content, pages);
        if (extracted is null)
            return null;

        data["_paginas_extraidas"] = true;
        data["_paginas_originales"] = totalPaginas;
        return Convert.ToBase64String(extracted);
    }

    /// <summary>Lee <c>paginas_documento</c> (array de enteros base 1) y <c>total_paginas</c> del JSON del análisis.</summary>
    private static bool TryReadPageSubset(JsonObject data, out List<int> pages, out int totalPaginas)
    {
        pages = [];
        totalPaginas = 0;

        if (data.TryGetPropertyValue("total_paginas", out var tp) &&
            tp is JsonValue tpv && tpv.TryGetValue<int>(out var total))
        {
            totalPaginas = total;
        }

        if (data.TryGetPropertyValue("paginas_documento", out var pd) && pd is JsonArray arr)
        {
            foreach (var el in arr)
            {
                if (el is JsonValue v && v.TryGetValue<int>(out var page))
                    pages.Add(page);
            }
        }

        return totalPaginas > 0;
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
