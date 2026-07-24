using System.Runtime.CompilerServices;

namespace Flit.Infrastructure.Documents.Branding;

/// <summary>
/// Registra la tipografía de marca FLIT (HU #10855) en cuanto se carga el ensamblado
/// <c>Flit.Infrastructure</c>, antes de cualquier generación o estampado de PDF.
/// <para>
/// PdfSharpCore expone un único <c>GlobalFontSettings.FontResolver</c> por proceso y lo
/// <b>bloquea tras el primer uso</b>. Fijarlo desde un <see cref="ModuleInitializerAttribute"/>
/// garantiza que el resolutor superset (<see cref="FlitFontResolver"/>) gane siempre la carrera,
/// tanto en producción como bajo la ejecución paralela de xUnit — evitando el error
/// "Must not change font resolver after it was once used".
/// </para>
/// </summary>
internal static class BrandingModuleInitializer
{
    // CA2255: el resolutor de fuentes global de PdfSharpCore debe fijarse antes del primer uso y se
    // bloquea después; un ModuleInitializer es la única forma de garantizarlo de manera determinista
    // (incl. la ejecución paralela de xUnit). Uso deliberado y sin efectos observables más allá del
    // registro de fuentes embebidas.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialize() => FlitFonts.EnsureRegistered();
}
