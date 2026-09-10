namespace Flit.Tramites.Application.Identity;

/// <summary>
/// Firma de archivo que PdfSharpCore / QuestPDF / ImageSharp pueden pintar. El stream crudo de un
/// XObject PDF (FlateDecode de píxeles) no califica.
/// </summary>
public static class IdentitySignatureImageFormat
{
    public static bool IsSupported(byte[]? bytes)
    {
        if (bytes is not { Length: >= 8 })
            return false;

        return IsPng(bytes) || IsJpeg(bytes);
    }

    public static bool IsPng(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 8
        && bytes[0] == 0x89
        && bytes[1] == 0x50
        && bytes[2] == 0x4E
        && bytes[3] == 0x47
        && bytes[4] == 0x0D
        && bytes[5] == 0x0A
        && bytes[6] == 0x1A
        && bytes[7] == 0x0A;

    public static bool IsJpeg(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
}
