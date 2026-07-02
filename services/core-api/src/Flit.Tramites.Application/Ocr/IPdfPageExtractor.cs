namespace Flit.Tramites.Application.Ocr;

/// <summary>
/// Extrae un subconjunto de páginas de un PDF multi-documento. Cuando el OCR detecta que el documento
/// solicitado ocupa sólo algunas páginas de un PDF que mezcla varios (factura + FUR + improntas…), se
/// recorta ese subconjunto para subir al expediente sólo lo relevante y no el PDF completo. Contrato en
/// Application; la implementación (PdfSharpCore) vive en Infrastructure.
/// </summary>
public interface IPdfPageExtractor
{
    /// <summary>
    /// Devuelve un nuevo PDF con sólo las <paramref name="pages"/> indicadas (números de página base 1),
    /// o null si no se puede extraer (PDF ilegible, excede el límite de páginas, o ninguna página válida).
    /// </summary>
    byte[]? ExtractPages(ReadOnlyMemory<byte> pdf, IReadOnlyList<int> pages);
}
