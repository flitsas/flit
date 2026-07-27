using Flit.Tramites.Application.Documents;

namespace Flit.Infrastructure.Documents.Fur;

/// <summary>
/// Rutas de las plantillas PDF blank del FUR (copiadas al output en build). HU #10920 — soporta múltiples
/// formatos (AUTOMOTOR/MAQUINARIA/REMOLQUES); cada formato tiene su par p1 (formulario) + p2 (instrucciones).
/// </summary>
public sealed class FurTemplatePaths
{
    // AUTOMOTOR conserva los nombres históricos (compatibilidad con HU #10256).
    public const string FormularioP1FileName = "fur-formulario-p1-blank.pdf";
    public const string InstruccionesP2FileName = "fur-instrucciones-p2-blank.pdf";

    public FurTemplatePaths(string templatesDirectory)
        : this(templatesDirectory, FormularioP1FileName, InstruccionesP2FileName)
    {
    }

    public FurTemplatePaths(string templatesDirectory, string formularioP1FileName, string instruccionesP2FileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templatesDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(formularioP1FileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(instruccionesP2FileName);
        TemplatesDirectory = templatesDirectory;
        FormularioP1 = Path.Combine(templatesDirectory, formularioP1FileName);
        InstruccionesP2 = Path.Combine(templatesDirectory, instruccionesP2FileName);
    }

    public string TemplatesDirectory { get; }
    public string FormularioP1 { get; }
    public string InstruccionesP2 { get; }

    /// <summary>Rutas de plantillas AUTOMOTOR (compatibilidad).</summary>
    public static FurTemplatePaths FromBaseDirectory(string baseDirectory) =>
        FromBaseDirectory(baseDirectory, FurTemplateFormat.Automotor);

    /// <summary>Rutas de plantillas del formato indicado.</summary>
    public static FurTemplatePaths FromBaseDirectory(string baseDirectory, FurTemplateFormat format)
    {
        var dir = Path.Combine(baseDirectory, "Documents", "Fur", "Templates");
        var (p1, p2) = FileNamesFor(format);
        return new FurTemplatePaths(dir, p1, p2);
    }

    /// <summary>Nombres de archivo p1/p2 por formato. AUTOMOTOR mantiene los históricos.</summary>
    public static (string P1, string P2) FileNamesFor(FurTemplateFormat format) => format switch
    {
        FurTemplateFormat.Maquinaria => ("fur-maquinaria-p1-blank.pdf", "fur-maquinaria-p2-blank.pdf"),
        FurTemplateFormat.Remolques => ("fur-remolques-p1-blank.pdf", "fur-remolques-p2-blank.pdf"),
        _ => (FormularioP1FileName, InstruccionesP2FileName),
    };

    /// <summary>¿Existen ambos PDF de plantilla en disco? (para saltar formatos aún no incorporados).</summary>
    public bool Exists() => File.Exists(FormularioP1) && File.Exists(InstruccionesP2);

    public void EnsureExists()
    {
        if (!File.Exists(FormularioP1))
            throw new FileNotFoundException($"Plantilla FUR p1 no encontrada: {FormularioP1}");
        if (!File.Exists(InstruccionesP2))
            throw new FileNotFoundException($"Plantilla FUR p2 no encontrada: {InstruccionesP2}");
    }
}
