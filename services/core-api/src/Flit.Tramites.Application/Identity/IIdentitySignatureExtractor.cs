namespace Flit.Tramites.Application.Identity;

/// <summary>PNG recortado de la rúbrica del certificado de identidad Kyverum.</summary>
public sealed record IdentitySignatureCrop(byte[] PngBytes);

/// <summary>
/// Extrae la imagen de firma manuscrita del PDF del certificado. Devuelve <c>null</c> si el PDF no
/// trae un XObject de imagen anclable (layout aplanado, mock, PDF vacío). No lanza por layout.
/// </summary>
public interface IIdentitySignatureExtractor
{
    IdentitySignatureCrop? TryExtract(byte[] pdfBytes);

    /// <summary>
    /// True si el PNG/JPEG guardado tiene tinta visible (no es el recorte negro opaco de Kyverum
    /// ni un recorte vacío tras quitar el fondo).
    /// </summary>
    bool IsUsableInk(byte[] imageBytes);
}
