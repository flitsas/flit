namespace Flit.Admin.Application.Companies.PersonalizedDocuments;

/// <summary>Resultado de abrir un PDF para verificar que es legible y no está cifrado.</summary>
public sealed record PdfInspectionResult(bool IsParseable, bool IsEncrypted, int PageCount);

/// <summary>
/// Puerto para abrir un PDF y verificar que es parseable, no está cifrado y contar sus páginas.
/// Contrato en Application; la implementación (PdfSharpCore) vive en Infrastructure — mismo patrón que
/// <c>IPdfPageExtractor</c> (Flit.Tramites.Application.Ocr).
/// </summary>
public interface IPdfDocumentInspector
{
    /// <summary>
    /// Abre <paramref name="bytes"/> en modo información (sin mutar). <c>IsParseable=false</c> si el
    /// PDF está corrupto/ilegible.
    /// </summary>
    PdfInspectionResult Inspect(byte[] bytes);
}
