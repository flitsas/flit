using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Enums;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Selecciona el orden del expediente consolidado (HU #10522), por FAMILIA del tipo (ADR-0050):
/// <list type="bullet">
///   <item>Matrículas y traspaso conservan su orden propio, que intercala los documentos generados
///   —FUR, licencia— con los subidos.</item>
///   <item>La familia OTROS usa el orden genérico (RF27/41) en vez de rechazarse.</item>
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
