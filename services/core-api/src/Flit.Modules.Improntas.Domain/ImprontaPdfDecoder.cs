namespace Flit.Modules.Improntas.Domain;

/// <summary>
/// Decodifica el PDF del Certificado de Improntas Digitales devuelto por Kyverum RUNT como Data URI
/// base64. Compartido entre el módulo admin y el flujo integrado de Trámites.
/// </summary>
public static class ImprontaPdfDecoder
{
    private const string PdfDataUriPrefix = "data:application/pdf;base64,";

    /// <summary>
    /// Recorta el prefijo <c>data:application/pdf;base64,</c> si está presente (defensivo: si Kyverum
    /// alguna vez devolviera el base64 puro, también funciona) y decodifica a bytes.
    /// </summary>
    public static byte[] Decode(string dataUri)
    {
        var base64 = dataUri.StartsWith(PdfDataUriPrefix, StringComparison.Ordinal)
            ? dataUri[PdfDataUriPrefix.Length..]
            : dataUri;
        return Convert.FromBase64String(base64);
    }
}
