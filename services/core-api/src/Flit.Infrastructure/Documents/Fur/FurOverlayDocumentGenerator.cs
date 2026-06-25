using System.Text.RegularExpressions;
using Flit.Tramites.Application.Documents;

namespace Flit.Infrastructure.Documents.Fur;

/// <summary>
/// Generador FUR por overlay PdfSharpCore sobre plantillas blank oficiales (sin Chromium/Handlebars).
/// Matrícula y traspaso: formulario p1 + instrucciones p2 compartidas.
/// </summary>
public sealed class FurOverlayDocumentGenerator : IFurDocumentGenerator
{
    private readonly FurFieldManifest _manifest;
    private readonly FurTemplatePaths _templates;

    public FurOverlayDocumentGenerator()
        : this(FurFieldManifestLoader.LoadEmbedded(), FurTemplatePaths.FromBaseDirectory(AppContext.BaseDirectory))
    {
    }

    internal FurOverlayDocumentGenerator(FurFieldManifest manifest, FurTemplatePaths templates)
    {
        _manifest = manifest;
        _templates = templates;
    }

    public GeneratedDocument GenerateFur(FurDocumentData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _templates.EnsureExists();

        var values = FurFieldMapper.Map(data);
        var p1Template = File.ReadAllBytes(_templates.FormularioP1);
        var p2Template = File.ReadAllBytes(_templates.InstruccionesP2);

        var p1 = FurOverlayRenderer.RenderPage1(p1Template, _manifest, values);
        var pdf = FurOverlayRenderer.MergePages(p1, p2Template);

        return new GeneratedDocument("fur", $"fur_{SafeRef(data.ReferenceNumber)}.pdf", "application/pdf", pdf);
    }

    public GeneratedDocument GenerateCompraventa(FurDocumentData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var bytes = FurCompraventaDocumentGenerator.Generate(data);
        return new GeneratedDocument(
            "compraventa",
            $"compraventa_{SafeRef(data.ReferenceNumber)}.pdf",
            "application/pdf",
            bytes);
    }

    private static string SafeRef(string reference) =>
        Regex.Replace(reference, @"[^\w\-.]", "-");
}
