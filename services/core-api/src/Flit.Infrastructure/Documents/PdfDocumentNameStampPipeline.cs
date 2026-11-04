using Flit.Infrastructure.Documents.Branding;
using Microsoft.Extensions.Logging;
using PdfSharpCore.Pdf.IO;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// HU #14209 — estampa el nombre del documento con reintento: stamp → N1 → stamp → N2 → stamp → sin pie.
/// Inyectable en tests vía delegados para simular fallos de Modify sin fixtures PDF hostiles.
/// </summary>
internal sealed partial class PdfDocumentNameStampPipeline
{
    private readonly ILogger _logger;
    private readonly Func<byte[], string, float, byte[]> _stamp;
    private readonly Func<byte[], byte[]?> _normalizeN1;
    private readonly Func<byte[], byte[]?> _normalizeN2;

    public PdfDocumentNameStampPipeline(ILogger logger)
        : this(
            logger,
            FlitPdfStamper.ApplyDocumentName,
            pdf => PdfModifyNormalizer.TryRewriteViaImport(pdf, out var n1) ? n1 : null,
            pdf => PdfModifyNormalizer.TryRasterizeFromPageImages(pdf, out var n2) ? n2 : null)
    {
    }

    internal PdfDocumentNameStampPipeline(
        ILogger logger,
        Func<byte[], string, float, byte[]> stamp,
        Func<byte[], byte[]?> normalizeN1,
        Func<byte[], byte[]?> normalizeN2)
    {
        _logger = logger;
        _stamp = stamp;
        _normalizeN1 = normalizeN1;
        _normalizeN2 = normalizeN2;
    }

    public byte[] Apply(byte[] pdf, string documentName, float bottomCm)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        if (string.IsNullOrWhiteSpace(documentName))
            return pdf;

        if (TryStamp(pdf, documentName, bottomCm, out var stamped, out var firstError))
            return stamped;

        var n1 = _normalizeN1(pdf);
        if (n1 is not null && TryStamp(n1, documentName, bottomCm, out stamped, out _))
        {
            LogStampOkAfterN1(_logger, documentName, firstError);
            return stamped;
        }

        var n2 = _normalizeN2(n1 ?? pdf);
        if (n2 is not null && TryStamp(n2, documentName, bottomCm, out stamped, out _))
        {
            LogStampOkAfterN2(_logger, documentName, firstError);
            return stamped;
        }

        LogStampFallbackWithoutFooter(_logger, documentName, firstError);
        // HU #14240 — nunca devolver bytes que Merge no pueda Import (Token '18', etc.).
        var candidate = n2 ?? n1 ?? pdf;
        var safe = PdfModifyNormalizer.EnsureImportable(candidate, documentName, out var substituted);
        if (substituted)
            LogStampFallbackPlaceholder(_logger, documentName, firstError);
        return safe;
    }

    private bool TryStamp(
        byte[] pdf,
        string documentName,
        float bottomCm,
        out byte[] stamped,
        out string? error)
    {
        try
        {
            stamped = _stamp(pdf, documentName, bottomCm);
            error = null;
            return true;
        }
        catch (Exception ex) when (IsStampFailure(ex))
        {
            stamped = [];
            error = ex.Message;
            return false;
        }
    }

    internal static bool IsStampFailure(Exception ex) =>
        ex is PdfReaderException
            or InvalidOperationException
            or IOException
            or ArgumentException
            or NotSupportedException
            or FormatException;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "HU14209: PDF no aceptó Modify; estampado OK tras normalización N1 (Import-rewrite). Documento={DocumentName}. Causa inicial={Error}")]
    private static partial void LogStampOkAfterN1(ILogger logger, string documentName, string? error);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "HU14209: PDF no aceptó Modify; estampado OK tras normalización N2 (raster). Documento={DocumentName}. Causa inicial={Error}")]
    private static partial void LogStampOkAfterN2(ILogger logger, string documentName, string? error);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "HU14209: no se pudo estampar pie tras N1/N2; se incluye la parte sin pie. Documento={DocumentName}. Causa={Error}")]
    private static partial void LogStampFallbackWithoutFooter(ILogger logger, string documentName, string? error);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "HU14240: PDF ilegible para Import tras N1/N2; se sustituye por página de aviso. Documento={DocumentName}. Causa={Error}")]
    private static partial void LogStampFallbackPlaceholder(ILogger logger, string documentName, string? error);
}
