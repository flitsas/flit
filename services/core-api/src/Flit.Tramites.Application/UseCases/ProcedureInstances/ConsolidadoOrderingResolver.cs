using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Enums;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Selecciona el orden del expediente consolidado (HU #10522). Unifica las tres estrategias:
/// <list type="bullet">
///   <item>Si se provee <paramref name="matrixOrder"/> (RF26, flag <c>ConsolidadoFromMatrix</c> ON),
///   ordena por esa prelación configurada.</item>
///   <item>Matrícula inicial y traspaso conservan su orden hard-coded actual (sin regresión).</item>
///   <item>Cualquier otra modalidad (RF27/41) usa el orden genérico en vez de rechazarse.</item>
/// </list>
/// </summary>
internal static class ConsolidadoOrderingResolver
{
    internal static IReadOnlyList<ProcedureInstanceAttachment> Select(
        IEnumerable<ProcedureInstanceAttachment> attachments,
        string? modalidadCode,
        IReadOnlyList<string>? matrixOrder = null)
    {
        if (matrixOrder is { Count: > 0 })
            return GenericConsolidadoOrdering.SelectByPrecedence(attachments, matrixOrder);

        return TramiteModalidadEntradaCodes.FromCode(modalidadCode) switch
        {
            TramiteModalidadEntrada.MatriculaInicial => MatriculaConsolidadoOrdering.SelectOrdered(attachments),
            TramiteModalidadEntrada.Traspaso => TraspasoConsolidadoOrdering.SelectOrdered(attachments),
            _ => GenericConsolidadoOrdering.SelectOrdered(attachments),
        };
    }
}
