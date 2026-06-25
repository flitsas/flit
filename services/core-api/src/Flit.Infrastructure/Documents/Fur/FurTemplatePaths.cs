namespace Flit.Infrastructure.Documents.Fur;

/// <summary>Rutas de plantillas PDF blank del FUR (copiadas al output en build).</summary>
public sealed class FurTemplatePaths
{
    public const string FormularioP1FileName = "fur-formulario-p1-blank.pdf";
    public const string InstruccionesP2FileName = "fur-instrucciones-p2-blank.pdf";

    public FurTemplatePaths(string templatesDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templatesDirectory);
        TemplatesDirectory = templatesDirectory;
        FormularioP1 = Path.Combine(templatesDirectory, FormularioP1FileName);
        InstruccionesP2 = Path.Combine(templatesDirectory, InstruccionesP2FileName);
    }

    public string TemplatesDirectory { get; }
    public string FormularioP1 { get; }
    public string InstruccionesP2 { get; }

    public static FurTemplatePaths FromBaseDirectory(string baseDirectory) =>
        new(Path.Combine(baseDirectory, "Documents", "Fur", "Templates"));

    public void EnsureExists()
    {
        if (!File.Exists(FormularioP1))
            throw new FileNotFoundException($"Plantilla FUR p1 no encontrada: {FormularioP1}");
        if (!File.Exists(InstruccionesP2))
            throw new FileNotFoundException($"Plantilla FUR p2 no encontrada: {InstruccionesP2}");
    }
}
