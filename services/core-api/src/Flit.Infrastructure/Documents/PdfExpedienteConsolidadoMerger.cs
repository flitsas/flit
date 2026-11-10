using Flit.Infrastructure.Documents.Branding;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Fusiona adjuntos en un PDF único. PDFs se importan con PdfSharpCore; imágenes se
/// renderizan a una página con QuestPDF (ya presente por HU #10256).
/// <para>HU #14505 / ADR-0083 — <see cref="NormalizeToPdf"/> clasifica por magic bytes
/// (precedencia sobre MIME) y reencodea PNG/JPEG vía <see cref="PngJpegImageDecoding"/>.</para>
/// </summary>
public sealed partial class PdfExpedienteConsolidadoMerger : IExpedienteConsolidadoMerger
{
    private readonly ILogger<PdfExpedienteConsolidadoMerger> _logger;
    private readonly PdfDocumentNameStampPipeline _stampPipeline;

    static PdfExpedienteConsolidadoMerger()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public PdfExpedienteConsolidadoMerger()
        : this(NullLogger<PdfExpedienteConsolidadoMerger>.Instance)
    {
    }

    public PdfExpedienteConsolidadoMerger(ILogger<PdfExpedienteConsolidadoMerger> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _stampPipeline = new PdfDocumentNameStampPipeline(logger);
    }

    /// <summary>Constructor de pruebas: permite inyectar el pipeline de estampado/normalización.</summary>
    internal PdfExpedienteConsolidadoMerger(PdfDocumentNameStampPipeline stampPipeline)
        : this(stampPipeline, NullLogger<PdfExpedienteConsolidadoMerger>.Instance)
    {
    }

    internal PdfExpedienteConsolidadoMerger(
        PdfDocumentNameStampPipeline stampPipeline,
        ILogger<PdfExpedienteConsolidadoMerger> logger)
    {
        _stampPipeline = stampPipeline ?? throw new ArgumentNullException(nameof(stampPipeline));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public byte[] NormalizeToPdf(byte[] content, string mimetype)
    {
        ArgumentNullException.ThrowIfNull(content);

        // HU #14505 / ADR-0083 — magic bytes mandan sobre el MIME declarado (FT1-0000372:
        // PDF real etiquetado image/jpeg → QuestPDF "Cannot decode the provided image").
        var kind = DetectContentKind(content);
        if (kind == AttachmentContentKind.Pdf)
            return content;

        if (kind is AttachmentContentKind.Jpeg or AttachmentContentKind.Png)
            return ImageBytesToPdfSafe(content);

        if (string.Equals(mimetype, "application/pdf", StringComparison.OrdinalIgnoreCase))
            return content;

        if (IsImageMime(mimetype))
            return ImageBytesToPdfSafe(content);

        throw new NotSupportedException($"Mimetype no soportado para consolidado: {mimetype}");
    }

    /// <summary>Clasificación por firma de archivo (sin confiar en MIME).</summary>
    internal enum AttachmentContentKind
    {
        Unknown = 0,
        Pdf = 1,
        Jpeg = 2,
        Png = 3,
    }

    internal static AttachmentContentKind DetectContentKind(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 4
            && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46)
            return AttachmentContentKind.Pdf;

        if (IdentitySignatureImageFormat.IsJpeg(bytes))
            return AttachmentContentKind.Jpeg;

        if (IdentitySignatureImageFormat.IsPng(bytes))
            return AttachmentContentKind.Png;

        return AttachmentContentKind.Unknown;
    }

    /// <summary>
    /// Reencode limpio (HU #13371) + QuestPDF; si falla, página de aviso Import-ok (sin lanzar).
    /// </summary>
    private byte[] ImageBytesToPdfSafe(byte[] imageBytes)
    {
        var clean = PngJpegImageDecoding.ReencodeToCleanPng(imageBytes);
        if (clean is null)
        {
            LogUndecodableImagePlaceholder(_logger);
            return CreateUndecodableImagePlaceholder();
        }

        try
        {
            return ImageToPdf(clean);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogUndecodableImagePlaceholder(_logger);
            return CreateUndecodableImagePlaceholder();
        }
    }

    /// <summary>Página de aviso cuando la imagen no se puede decodificar (HU #14505).</summary>
    internal static byte[] CreateUndecodableImagePlaceholder() =>
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(48);
                page.DefaultTextStyle(x => x.FontSize(11));
                page.Content().Column(col =>
                {
                    col.Spacing(12);
                    col.Item().Text("Imagen no incluida en el consolidado").Bold().FontSize(14);
                    col.Item().Text(
                        "El archivo de imagen no pudo procesarse (formato ilegible o incompatible). " +
                        "Reemplace el adjunto por un PNG o JPEG estándar, o por un PDF, y regenere el consolidado.");
                });
            });
        }).GeneratePdf();

    public byte[] Merge(IReadOnlyList<byte[]> pdfParts)
    {
        ArgumentNullException.ThrowIfNull(pdfParts);
        if (pdfParts.Count == 0)
            throw new ArgumentException("Se requiere al menos un PDF para fusionar.", nameof(pdfParts));

        using var output = new PdfDocument();
        for (var index = 0; index < pdfParts.Count; index++)
        {
            // HU #14240 / #14312 — nunca pasar a Import un PDF que PdfSharp no puede leer (Token '18', etc.).
            var safe = PdfModifyNormalizer.EnsureImportable(pdfParts[index], documentName: null, out var recovery);
            if (recovery == PdfImportRecovery.Converted)
                LogMergeConvertedUnimportablePart(_logger, index);
            else if (recovery == PdfImportRecovery.Placeholder)
                LogMergeSubstitutedUnimportablePart(_logger, index);

            using var inputStream = new MemoryStream(safe);
            using var input = PdfReader.Open(inputStream, PdfDocumentOpenMode.Import);
            for (var i = 0; i < input.PageCount; i++)
                output.AddPage(input.Pages[i]);
        }

        using var result = new MemoryStream();
        output.Save(result, false);
        return result.ToArray();
    }

    public byte[] Compose(MergeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Parts is null || request.Parts.Count == 0)
            throw new ArgumentException("Se requiere al menos una parte para componer el expediente.", nameof(request));

        // Portada (HU #10857) antepuesta como primera página, en Carta con membrete FLIT (sin pie).
        var parts = new List<byte[]>(request.Parts.Count + 1);
        if (request.Cover is not null)
            parts.Add(FlitCoverPageGenerator.Generate(ToCoverData(request.Cover)));

        // HU #10858 — pie con el nombre del documento en cada parte antes de fusionar.
        // HU #14209 — si Modify falla: N1 Import-rewrite → N2 raster → fallback.
        // HU #14240 — partes sin DocumentName también deben ser Import-ok antes del Merge.
        foreach (var part in request.Parts)
        {
            if (string.IsNullOrWhiteSpace(part.DocumentName))
            {
                var safe = PdfModifyNormalizer.EnsureImportable(part.Pdf, documentName: null, out var recovery);
                if (recovery == PdfImportRecovery.Converted)
                    LogComposeConvertedUnnamedPart(_logger);
                else if (recovery == PdfImportRecovery.Placeholder)
                    LogComposeSubstitutedUnnamedPart(_logger);
                parts.Add(safe);
            }
            else
            {
                parts.Add(_stampPipeline.Apply(part.Pdf, part.DocumentName!, BottomCmFor(part.Profile)));
            }
        }

        var merged = Merge(parts);

        // HU #10858 — marca de agua con el estado, salvo en estados finales (aprobado/entregado/preparado).
        if (ShouldWatermark(request.EstadoTramite))
            merged = FlitPdfStamper.ApplyWatermark(merged, request.EstadoTramite!.Trim().ToUpperInvariant());

        return merged;
    }

    // Punto 5: la marca de agua aparece salvo cuando el trámite está en un estado "final".
    private static readonly HashSet<string> EstadosSinMarcaAgua = new(StringComparer.OrdinalIgnoreCase)
    {
        "aprobado",
        "entregado",
        "preparado",
    };

    // ADR-0049: el margen inferior del pie es propiedad del tipo de documento, no del tema global.
    // internal (no private): Flit.Infrastructure.Tests (InternalsVisibleTo) prueba directamente que
    // el perfil de la parte resuelve al margen correcto ANTES de invocar FlitPdfStamper — la
    // comparación de bytes del PDF compuesto completo no es viable como prueba de regresión porque
    // PdfSharpCore asigna una etiqueta de subconjunto de fuente aleatoria en cada Save(), así que dos
    // invocaciones independientes de ApplyDocumentName con el MISMO texto nunca producen los mismos
    // bytes exactos aunque la geometría sea idéntica.
    internal static float BottomCmFor(StampProfile profile) => profile switch
    {
        StampProfile.Formulario => FlitDocumentTheme.DocNameBottomFormularioCm,
        _ => FlitDocumentTheme.DocNameBottomCm,
    };

    private static bool ShouldWatermark(string? estado) =>
        !string.IsNullOrWhiteSpace(estado) && !EstadosSinMarcaAgua.Contains(estado.Trim());

    private static FlitCoverData ToCoverData(ExpedienteCoverInfo cover) =>
        new(
            CodigoTramite: cover.CodigoTramite ?? string.Empty,
            Placa: cover.Placa ?? string.Empty,
            TipoTramite: cover.TipoTramite ?? string.Empty,
            SecretariaTransito: cover.SecretariaTransito ?? string.Empty,
            CompaniaRadicadora: cover.CompaniaRadicadora ?? string.Empty);

    private static bool IsImageMime(string mimetype) =>
        string.Equals(mimetype, "image/jpeg", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mimetype, "image/png", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mimetype, "image/webp", StringComparison.OrdinalIgnoreCase);

    private static byte[] ImageToPdf(byte[] imageBytes) =>
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(24);
                page.Content().Image(imageBytes).FitArea();
            });
        }).GeneratePdf();

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "HU14505: imagen de adjunto no decodificable; se sustituye por página de aviso en el consolidado.")]
    private static partial void LogUndecodableImagePlaceholder(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "HU14312: parte {PartIndex} del Merge no abre en Import; convertida vía PDFium→QuestPDF.")]
    private static partial void LogMergeConvertedUnimportablePart(ILogger logger, int partIndex);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "HU14240: parte {PartIndex} del Merge no abre en Import; sustituida por página de aviso.")]
    private static partial void LogMergeSubstitutedUnimportablePart(ILogger logger, int partIndex);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "HU14312: parte sin DocumentName no abre en Import; convertida vía PDFium→QuestPDF.")]
    private static partial void LogComposeConvertedUnnamedPart(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "HU14240: parte sin DocumentName no abre en Import; sustituida por página de aviso.")]
    private static partial void LogComposeSubstitutedUnnamedPart(ILogger logger);
}
