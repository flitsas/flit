using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Flags de unificación de la deuda técnica documental (HU #10522). <b>Todos OFF por defecto</b>:
/// con la configuración vacía el comportamiento es idéntico al actual (sin regresión). Se activan
/// por entorno cuando se validen en DEV. Sección de configuración: <c>DocumentBehavior</c>.
/// </summary>
public sealed class DocumentBehaviorOptions
{
    public const string SectionName = "DocumentBehavior";

    /// <summary>RF26 — ordenar el consolidado desde la matriz documental configurada (vs. hard-coded).</summary>
    public bool ConsolidadoFromMatrix { get; set; }

    /// <summary>RF17 — derivar la obligatoriedad del checklist desde el snapshot inmutable (vs. catálogo plano).</summary>
    public bool ChecklistFromSnapshot { get; set; }

    /// <summary>RF22 — resolver el orden documental desde el modelo unificado (order-overrides + prelación OT).</summary>
    public bool UnifiedOrder { get; set; }
}

/// <summary>
/// Puerto opcional (RF26) que provee la prelación documental resuelta desde la matriz configurada
/// para un trámite. Cuando el flag <see cref="DocumentBehaviorOptions.ConsolidadoFromMatrix"/> está
/// activo y este puerto devuelve una lista no vacía, el consolidado ordena por ella.
/// </summary>
public interface IConsolidadoMatrixOrderProvider
{
    Task<IReadOnlyList<string>?> GetOrderAsync(ProcedureInstance instance, CancellationToken ct = default);
}
