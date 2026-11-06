namespace Flit.Comparendos.Api.Infraestructura.Archivos;

/// <summary>
/// HU #14366 / GDC ID11: prepara PDFs antes del almacén. Política acordada: no transformar evidencia OCR
/// (fidelidad de auditoría); otros dominios quedan listos para una optimización sin pérdida cuando haya
/// herramienta aprobada (hoy se conserva el original).
/// </summary>
public static class OptimizadorPdf
{
    public const string ContentTypePdf = "application/pdf";

    /// <summary>Dominios cuya evidencia no se reescribe (OCR / originales de lectura).</summary>
    public static bool ExcluirDominio(string dominio) =>
        dominio.Equals("ocr", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Devuelve los bytes a persistir. OCR y no-PDF pasan intactos. PDF de otros dominios se conserva
    /// tal cual hasta habilitar linealización/compresión sin pérdida.
    /// </summary>
    public static byte[] Preparar(string dominio, string contentType, byte[] contenido)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        if (!contentType.Equals(ContentTypePdf, StringComparison.OrdinalIgnoreCase) || ExcluirDominio(dominio))
            return contenido;
        return contenido;
    }
}
