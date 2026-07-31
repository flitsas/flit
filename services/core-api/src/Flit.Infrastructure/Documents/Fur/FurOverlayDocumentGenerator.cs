using System.Text.RegularExpressions;
using Flit.Tramites.Application.Documents;

namespace Flit.Infrastructure.Documents.Fur;

/// <summary>
/// Generador FUR por overlay PdfSharpCore sobre plantillas blank oficiales (sin Chromium/Handlebars).
/// HU #10920 (Feature #10918) — multi-plantilla: selecciona manifest + plantillas + mapper según
/// <see cref="FurDocumentData.TemplateFormat"/> (AUTOMOTOR/MAQUINARIA/REMOLQUES). Un formato aún no
/// incorporado (sin manifest embebido o sin PDF en disco) cae a AUTOMOTOR. Matrícula y traspaso comparten
/// el par p1+p2 de su formato; la diferencia entre ambos es qué casillas marca el mapper.
/// </summary>
public sealed class FurOverlayDocumentGenerator : IFurDocumentGenerator
{
    private readonly Dictionary<FurTemplateFormat, FurFormatAssets> _byFormat;

    // HU #10256 fix — PdfSharpCore resuelve "Arial" contra las fuentes del SO y el runtime alpine no tiene
    // ninguna. Registrar el resolutor con fuente embebida antes de cualquier render (idempotente) hace la
    // generación independiente del SO.
    static FurOverlayDocumentGenerator() => FurFontResolver.EnsureRegistered();

    public FurOverlayDocumentGenerator()
        : this(AppContext.BaseDirectory)
    {
    }

    internal FurOverlayDocumentGenerator(string baseDirectory)
    {
        _byFormat = BuildRegistry(baseDirectory);
    }

    // Ctor de tests (compat HU #10256): inyecta directamente el manifest + plantillas AUTOMOTOR.
    internal FurOverlayDocumentGenerator(FurFieldManifest manifest, FurTemplatePaths templates)
    {
        _byFormat = new Dictionary<FurTemplateFormat, FurFormatAssets>
        {
            [FurTemplateFormat.Automotor] = new(manifest, templates, FurFieldMapper.Map),
        };
    }

    public GeneratedDocument GenerateFur(FurDocumentData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        // Formato solicitado; si aún no está incorporado (sin assets registrados), cae a AUTOMOTOR.
        var assets = _byFormat.TryGetValue(data.TemplateFormat, out var a)
            ? a
            : _byFormat[FurTemplateFormat.Automotor];

        assets.Templates.EnsureExists();
        var values = assets.Mapper(data);
        var p1Template = File.ReadAllBytes(assets.Templates.FormularioP1);
        var p2Template = File.ReadAllBytes(assets.Templates.InstruccionesP2);

        var p1 = FurOverlayRenderer.RenderPage1(p1Template, assets.Manifest, values);
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

    // Registra un formato solo si tiene manifest embebido Y plantillas en disco (así los formatos que aún
    // no se incorporaron simplemente no se registran y caen a AUTOMOTOR).
    private static Dictionary<FurTemplateFormat, FurFormatAssets> BuildRegistry(string baseDirectory)
    {
        var dict = new Dictionary<FurTemplateFormat, FurFormatAssets>();
        foreach (var format in new[] { FurTemplateFormat.Automotor, FurTemplateFormat.Maquinaria, FurTemplateFormat.Remolques })
        {
            if (!FurFieldManifestLoader.HasEmbedded(format))
                continue;

            var templates = FurTemplatePaths.FromBaseDirectory(baseDirectory, format);
            if (!templates.Exists())
                continue;

            dict[format] = new FurFormatAssets(
                FurFieldManifestLoader.LoadEmbedded(format),
                templates,
                MapperFor(format));
        }
        return dict;
    }

    // Mapper por formato. MAQUINARIA/REMOLQUES tendrán el suyo en #10922/#10923; hoy todos usan el de
    // AUTOMOTOR (que es el único formato registrado).
    private static Func<FurDocumentData, IReadOnlyDictionary<string, FurFieldValue>> MapperFor(FurTemplateFormat format) =>
        format switch
        {
            _ => FurFieldMapper.Map,
        };

    private static string SafeRef(string reference) =>
        Regex.Replace(reference, @"[^\w\-.]", "-");

    private sealed record FurFormatAssets(
        FurFieldManifest Manifest,
        FurTemplatePaths Templates,
        Func<FurDocumentData, IReadOnlyDictionary<string, FurFieldValue>> Mapper);
}
