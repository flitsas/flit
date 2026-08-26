using System.Security.Cryptography;

namespace Flit.Admin.Application.Companies.PersonalizedDocuments;

/// <summary>
/// Resultado de validar la integridad del PDF confirmado (HU #11313, §7 DT-6 del plan técnico).
/// </summary>
public sealed record PdfIntegrityCheck(
    bool IsValid,
    string? ErrorCode,
    string? ErrorMessage,
    string? Sha256,
    int PageCount)
{
    public static PdfIntegrityCheck Success(string sha256, int pageCount) =>
        new(true, null, null, sha256, pageCount);

    public static PdfIntegrityCheck Failure(string code, string message, string? sha256 = null) =>
        new(false, code, message, sha256, 0);
}

/// <summary>
/// Validación en servidor del PDF cargado, ÚNICA oportunidad de ver los bytes (HU #11313). Nunca
/// confía en el <c>content-type</c> ni en el SHA-256 que declara el cliente: recalcula el hash y
/// verifica magic bytes, cifrado, parseabilidad, tamaño y páginas — en ese orden, para no gastar
/// CPU abriendo el PDF si ya falló algo más barato de comprobar.
/// </summary>
public sealed class PdfIntegrityValidator
{
    /// <summary>Tamaño máximo permitido: 20 MB, en paridad con <c>document_types.max_size_bytes</c>.</summary>
    public const long MaxSizeBytes = 20L * 1024 * 1024;

    /// <summary>Páginas máximas permitidas.</summary>
    public const int MaxPages = 30;

    private static readonly byte[] PdfMagic = [0x25, 0x50, 0x44, 0x46, 0x2D]; // "%PDF-"

    // "/Encrypt" — token del diccionario trailer que marca un PDF cifrado (user u owner password).
    // PdfSharpCore solo expone SecuritySettings.DocumentSecurityLevel al CREAR/escribir un documento;
    // al RE-ABRIR uno ya cifrado con owner-password-only (que no exige contraseña para abrir) el
    // reader no lo repuebla. El escaneo de bytes es la señal fiable y barata: no requiere abrir el
    // PDF, y el token aparece en texto plano en el trailer sin comprimir de cualquier PDF cifrado.
    private static readonly byte[] EncryptMarker = "/Encrypt"u8.ToArray();

    private readonly IPdfDocumentInspector _inspector;

    public PdfIntegrityValidator(IPdfDocumentInspector inspector)
    {
        _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    /// <summary>
    /// Valida <paramref name="bytes"/> contra el <paramref name="declaredSha256"/> que el cliente
    /// aportó en el alta. El SHA-256 recalculado se devuelve SIEMPRE (incluso en fallo posterior a su
    /// cálculo), para que el caller pueda decidir si conservarlo con fines de diagnóstico.
    /// </summary>
    public PdfIntegrityCheck Validate(byte[] bytes, string declaredSha256)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaredSha256);

        if (bytes.LongLength > MaxSizeBytes)
        {
            return PdfIntegrityCheck.Failure("excede_tamano", "El PDF excede el tamaño máximo permitido (20 MB).");
        }

        if (bytes.Length < PdfMagic.Length || !bytes.AsSpan(0, PdfMagic.Length).SequenceEqual(PdfMagic))
        {
            return PdfIntegrityCheck.Failure("no_es_pdf", "El archivo no tiene la firma de un PDF válido.");
        }

        var actualSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(actualSha256, declaredSha256, StringComparison.OrdinalIgnoreCase))
        {
            return PdfIntegrityCheck.Failure(
                "sha256_mismatch", "El hash recalculado en servidor no coincide con el declarado.", actualSha256);
        }

        if (bytes.AsSpan().IndexOf(EncryptMarker) >= 0)
        {
            return PdfIntegrityCheck.Failure("pdf_cifrado", "El PDF está cifrado.", actualSha256);
        }

        var inspection = _inspector.Inspect(bytes);

        if (!inspection.IsParseable)
        {
            return PdfIntegrityCheck.Failure("pdf_ilegible", "El PDF no se pudo abrir.", actualSha256);
        }

        if (inspection.IsEncrypted)
        {
            return PdfIntegrityCheck.Failure("pdf_cifrado", "El PDF está cifrado.", actualSha256);
        }

        if (inspection.PageCount > MaxPages)
        {
            return PdfIntegrityCheck.Failure(
                "excede_paginas", $"El PDF excede el máximo de {MaxPages} páginas.", actualSha256);
        }

        return PdfIntegrityCheck.Success(actualSha256, inspection.PageCount);
    }
}
