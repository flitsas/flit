using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Enums;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Selecciona el orden del expediente consolidado (HU #10522):
/// <list type="bullet">
///   <item>Matrícula inicial y traspaso conservan su orden por modalidad (que sí ordena los
///   documentos generados —FUR, licencia— junto a los subidos).</item>
///   <item>Cualquier otra modalidad (RF27/41) usa el orden genérico en vez de rechazarse.</item>
/// </list>
/// </summary>
internal static class ConsolidadoOrderingResolver
{
    internal static IReadOnlyList<ProcedureInstanceAttachment> Select(
        IEnumerable<ProcedureInstanceAttachment> attachments,
        string? modalidadCode) =>
        ProcedureFamilyCodes.FromCodeOrLegacyModalidad(modalidadCode) switch
        {
            ProcedureFamily.Matriculas => MatriculaConsolidadoOrdering.SelectOrdered(attachments),
            ProcedureFamily.Traspaso => TraspasoConsolidadoOrdering.SelectOrdered(attachments),
            _ => GenericConsolidadoOrdering.SelectOrdered(attachments),
        };
}
