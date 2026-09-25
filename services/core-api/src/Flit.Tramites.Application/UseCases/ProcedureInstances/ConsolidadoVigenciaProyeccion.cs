using Flit.Queries.Domain.Documentos;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #12791 (Épica #12760) — adapta un <see cref="ProcedureInstance"/> a
/// <see cref="ConsolidadoVigencia.Derivar"/>: bandera + sello de la instancia (HU #12790), estado final,
/// migrado V1 y <c>Source</c> del adjunto del tipo. No consulta nada: el llamador aporta los adjuntos
/// (listado: el grafo ya cargado; detalle: <c>GetConsolidadoSourcesAsync</c>).
/// </summary>
/// <remarks>Uso de ejemplo: <c>var (wizard, maestro) = ConsolidadoVigenciaProyeccion.Desde(instance, sources);</c></remarks>
public static class ConsolidadoVigenciaProyeccion
{
    /// <summary>
    /// Vigencia del consolidado del wizard a partir de los <c>Attachments</c> ya cargados en la instancia
    /// (el adjunto más reciente del tipo, mismo criterio que <c>ConsolidadoEntregaModos.Existente</c>).
    /// </summary>
    public static ConsolidadoVigenciaDto Wizard(ProcedureInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return Wizard(instance, SourceMasReciente(instance, ConsolidadoVigencia.TipoWizard));
    }

    /// <summary>Wizard y maestro a partir del diccionario tipo → Source (tipo ausente = sin PDF).</summary>
    public static (ConsolidadoVigenciaDto Wizard, ConsolidadoVigenciaDto Maestro) Desde(
        ProcedureInstance instance,
        IReadOnlyDictionary<string, string>? sourcesPorTipo)
    {
        ArgumentNullException.ThrowIfNull(instance);
        string? wizardSource = null;
        string? maestroSource = null;
        if (sourcesPorTipo is not null)
        {
            // Búsqueda sin distinguir mayúsculas aunque el diccionario llegue con el comparador por defecto.
            foreach (var (tipo, source) in sourcesPorTipo)
            {
                if (string.Equals(tipo, ConsolidadoVigencia.TipoWizard, StringComparison.OrdinalIgnoreCase))
                    wizardSource = source;
                else if (string.Equals(tipo, ConsolidadoVigencia.TipoMaestro, StringComparison.OrdinalIgnoreCase))
                    maestroSource = source;
            }
        }

        return (Wizard(instance, wizardSource), Maestro(instance, maestroSource));
    }

    private static ConsolidadoVigenciaDto Wizard(ProcedureInstance instance, string? source) =>
        ConsolidadoVigencia.Derivar(
            source,
            instance.ConsolidadoWizardVigente,
            instance.ConsolidadoWizardGeneradoEn,
            TramiteEstado.EsFinal(instance.Status),
            instance.IsMigrated);

    private static ConsolidadoVigenciaDto Maestro(ProcedureInstance instance, string? source) =>
        ConsolidadoVigencia.Derivar(
            source,
            instance.ConsolidadoMaestroVigente,
            instance.ConsolidadoMaestroGeneradoEn,
            TramiteEstado.EsFinal(instance.Status),
            instance.IsMigrated);

    private static string? SourceMasReciente(ProcedureInstance instance, string tipo) =>
        instance.Attachments
            .Where(a => string.Equals(a.Tipo, tipo, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.UploadedAt)
            .FirstOrDefault()?.Source;
}
