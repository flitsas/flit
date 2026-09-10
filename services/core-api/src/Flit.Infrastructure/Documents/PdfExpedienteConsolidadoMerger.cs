using Flit.Infrastructure.Documents.Branding;
using Flit.Tramites.Application.Documents;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Fusiona adjuntos en un PDF único. PDFs se importan con PdfSharpCore; imágenes se
/// renderizan a una página con QuestPDF (ya presente por HU #10256).
/// </summary>
public sealed class PdfExpedienteConsolidadoMerger : IExpedienteConsolidadoMerger
{
    static PdfExpedienteConsolidadoMerger()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] NormalizeToPdf(byte[] content, string mimetype)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (string.Equals(mimetype, "application/pdf", StringComparison.OrdinalIgnoreCase))
            return content;

        if (IsImageMime(mimetype))
            return ImageToPdf(content);

        throw new NotSupportedException($"Mimetype no soportado para consolidado: {mimetype}");
    }

    public byte[] Merge(IReadOnlyList<byte[]> pdfParts)
    {
        ArgumentNullException.ThrowIfNull(pdfParts);
        if (pdfParts.Count == 0)
            throw new ArgumentException("Se requiere al menos un PDF para fusionar.", nameof(pdfParts));

        using var output = new PdfDocument();
        foreach (var pdfBytes in pdfParts)
        {
            using var inputStream = new MemoryStream(pdfBytes);
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

        // HU #10858 — pie con el nombre del documento en cada parte antes de fusionar (se estampa
        // por parte para que cada página lleve la descripción de SU documento). El margen inferior
        // depende del perfil de estampado de la parte (ADR-0049): el FUR usa un margen reducido
        // porque su contenido oficial llega más cerca del borde inferior que el resto de documentos.
        foreach (var part in request.Parts)
            parts.Add(string.IsNullOrWhiteSpace(part.DocumentName)
                ? part.Pdf
                : FlitPdfStamper.ApplyDocumentName(part.Pdf, part.DocumentName!, BottomCmFor(part.Profile)));

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
}
