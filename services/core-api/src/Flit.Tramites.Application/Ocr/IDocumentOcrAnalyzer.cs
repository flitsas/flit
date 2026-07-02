using System.Text.Json.Nodes;

namespace Flit.Tramites.Application.Ocr;

/// <summary>
/// Resultado de analizar un documento con OCR semántico (lectura por LLM de visión). En éxito
/// <see cref="Data"/> trae el JSON extraído; en fallo, <see cref="Status"/> + <see cref="Message"/>
/// llevan un código HTTP claro (503 degradación) y un mensaje apto para mostrar al operador
/// ("adjunta el documento manualmente").
/// </summary>
/// <param name="Ok">true → análisis correcto y <see cref="Data"/> presente.</param>
/// <param name="Data">JSON extraído por el modelo (campos por tipo). null si <see cref="Ok"/> es false.</param>
/// <param name="Status">Código HTTP sugerido cuando falla (503 provider caído, 500 parseo, 400 tipo).</param>
/// <param name="Message">Mensaje legible para el usuario cuando falla.</param>
public sealed record DocumentOcrAnalysis(bool Ok, JsonObject? Data, int Status = 200, string? Message = null);

/// <summary>
/// Contrato del analizador OCR de documentos de trámites. Implementación por defecto es un MOCK
/// determinista (<see cref="MockDocumentOcrAnalyzer"/>); reemplazable por el analizador real
/// (<c>AnthropicDocumentOcrAnalyzer</c> en Infrastructure) vía <c>Ocr:Provider = anthropic</c> sin
/// tocar los handlers — mismo patrón contract-first que <c>IBiometricScorer</c> / los consultation
/// providers, pero asíncrono porque hace IO (llamada al modelo de visión).
/// </summary>
public interface IDocumentOcrAnalyzer
{
    /// <summary>Analiza el binario del documento con el prompt del tipo y devuelve el JSON extraído.</summary>
    /// <param name="tipo">Tipo de documento: factura | aduana | impronta | soat.</param>
    /// <param name="content">Bytes del archivo (ya validado tamaño/magic bytes por el handler).</param>
    /// <param name="mediaType">MIME resuelto por magic bytes: application/pdf | image/jpeg | image/png.</param>
    Task<DocumentOcrAnalysis> AnalyzeAsync(string tipo, ReadOnlyMemory<byte> content, string mediaType, CancellationToken ct);
}
