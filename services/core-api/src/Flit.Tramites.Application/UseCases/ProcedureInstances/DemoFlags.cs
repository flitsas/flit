namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Toggles SOLO para la demo del MVP de matrícula. Permiten recorrer la ruta de
/// inicio a fin sin fricción (sin subir adjuntos). Default OFF: en ausencia de la
/// variable de entorno el comportamiento es el estricto de producción.
/// DEUDA: remover/endurecer tras la presentación.
/// </summary>
internal static class DemoFlags
{
    /// <summary>
    /// <c>TRAMITES_DEMO_RELAX_DOCS=true</c> → los documentos obligatorios dejan de
    /// bloquear el avance del wizard, los blockers y el hard-gate de submit.
    /// </summary>
    public static bool RelaxDocs =>
        string.Equals(
            Environment.GetEnvironmentVariable("TRAMITES_DEMO_RELAX_DOCS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
}
